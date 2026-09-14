using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using AwesomeAssertions;
using Dapper;
using FoodDeliveryService.Common.Domain;
using FoodDeliveryService.Modules.Payments.Application.Abstractions.Payments;
using FoodDeliveryService.Modules.Payments.Application.PaymentMethods.GetPaymentMethods;
using FoodDeliveryService.Modules.Payments.IntegrationTests.Abstractions;
using Npgsql;

namespace FoodDeliveryService.Modules.Payments.IntegrationTests.Webhooks;

/// <summary>
/// Stripe's webhook ingress, end to end — Feature 3.8 Milestone E, §7.
/// <para>
/// <b>The signature is verified for real here.</b> <c>IPaymentWebhookParser</c> is the one piece of
/// the Stripe seam this suite does <em>not</em> substitute: it is the only authentication the
/// endpoint has, and faking it would leave "the endpoint is not simply open to the internet"
/// untested. The tests sign their own payloads under the fixture secret exactly as Stripe does.
/// </para>
/// <para>
/// Everything after the signature is the real path: the real anonymous route, the real event log and
/// its unique index, the real outbox tick, and the real card attachment three hops later.
/// </para>
/// </summary>
public class StripeWebhookTests(IntegrationTestWebAppFactory factory) : BaseIntegrationTest(factory)
{
    private const string WebhookPath = "payments/webhooks/stripe";
    private const string SignatureHeader = "Stripe-Signature";

    private static readonly TimeSpan ProjectionTimeout = TimeSpan.FromSeconds(30);

    [Fact]
    public async Task Webhook_Should_RecordAndAttachTheCard()
    {
        // Arrange — the customer's Stripe customer object already exists; it was created eagerly
        // when they registered (§6.2), which is what a SetupIntent confirmed in a browser attaches
        // to. The pm_… below is what Stripe.js would have collected.
        string stripeCustomerId = await WaitForStripeCustomerAsync(Factory.CustomerUserId);
        string eventId = NewEventId();
        const string paymentMethodId = "pm_card_webhook_attach";

        string payload = SetupIntentSucceeded(eventId, stripeCustomerId, paymentMethodId);

        // Act — no token, because Stripe has none to give.
        HttpResponseMessage response = await PostWebhookAsync(payload);

        // Assert — the endpoint returns immediately (§7.6): the row is written and the work is the
        // outbox's.
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        Result<string> recorded = await Poller.WaitAsync(ProjectionTimeout, () => EventTypeAsync(eventId));

        recorded.IsSuccess.Should().BeTrue("a verified event must be recorded");
        recorded.Value.Should().Be(PaymentWebhookEventTypes.SetupIntentSucceeded);

        // The card itself, an outbox tick later — this is the attachment, not the endpoint that
        // created the SetupIntent (§1.3 step 1).
        Result<string> saved = await Poller.WaitAsync(
            ProjectionTimeout,
            () => SavedCardAsync(Factory.CustomerUserId, paymentMethodId));

        saved.IsSuccess.Should().BeTrue("setup_intent.succeeded is what saves the card");

        Factory.PaymentGateway
            .CallsFor(FakeGatewayOperation.AttachPaymentMethod)
            .Should().Contain(call => call.IdempotencyKey ==
                PaymentIdempotencyKeys.AttachPaymentMethod(paymentMethodId));

        // And the log row is closed, so the table's outstanding rows mean "owed work".
        Result<bool> processed = await Poller.WaitAsync(ProjectionTimeout, () => ProcessedAsync(eventId));

        processed.IsSuccess.Should().BeTrue("an event that was acted on must be marked processed");

        // The card is on the customer's own list, with display fields and no Stripe identifier.
        HttpClient client = await CreateCustomerClientAsync();

        List<PaymentMethodResponse>? cards = await client.GetFromJsonAsync<List<PaymentMethodResponse>>(
            new Uri("payments/payment-methods", UriKind.Relative),
            TestContext.Current.CancellationToken);

        cards.Should().ContainSingle();
        cards![0].Brand.Should().Be("visa");
    }

    [Fact]
    public async Task Webhook_Should_AcknowledgeARedelivery_WithoutRecordingItTwice()
    {
        // Arrange — Stripe redelivers on any non-2xx and on its own schedule (§7.4). The inbox is
        // the wrong table for that; stripe_event_logs and its unique index are the right one.
        string stripeCustomerId = await WaitForStripeCustomerAsync(Factory.CustomerUserId);
        string eventId = NewEventId();

        string payload = SetupIntentSucceeded(eventId, stripeCustomerId, "pm_card_webhook_redelivery");

        // Act
        HttpResponseMessage first = await PostWebhookAsync(payload);
        HttpResponseMessage second = await PostWebhookAsync(payload);

        // Assert — "already handled" is exactly what a 2xx means to Stripe. An error on the second
        // delivery would earn a third one, for an event that has already been acted on.
        first.StatusCode.Should().Be(HttpStatusCode.NoContent);
        second.StatusCode.Should().Be(HttpStatusCode.NoContent);

        Result<int> rows = await Poller.WaitAsync(ProjectionTimeout, () => EventCountAsync(eventId));

        rows.IsSuccess.Should().BeTrue();
        rows.Value.Should().Be(1, "one provider event is one row, however many times it is delivered");
    }

    [Fact]
    public async Task Webhook_Should_Be400_WhenTheSignatureDoesNotVerify()
    {
        // Arrange — a well-formed event, signed with the wrong secret. This is the forged request,
        // and it is also what a webhook secret from the wrong environment looks like.
        string eventId = NewEventId();
        string payload = SetupIntentSucceeded(eventId, "cus_forged", "pm_forged");

        // Act
        HttpResponseMessage response = await PostWebhookAsync(
            payload,
            Sign(payload, secret: "whsec_the_wrong_secret"));

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        // And nothing was written. The signature check runs before the row, so an unverified payload
        // never reaches the database at all.
        int rows = await CountAsync(eventId);

        rows.Should().Be(0, "an unverified payload is not recorded");
    }

    [Fact]
    public async Task Webhook_Should_Be400_WithNoSignatureAtAll()
    {
        // Arrange
        string payload = SetupIntentSucceeded(NewEventId(), "cus_unsigned", "pm_unsigned");

        // Act
        HttpResponseMessage response = await PostWebhookAsync(payload, signature: string.Empty);

        // Assert — 400 rather than 401: this endpoint is anonymous by design, and the missing thing
        // is a signature, not a token. A 401 would send Stripe looking for credentials it cannot have.
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Webhook_Should_RecordAnEventItDoesNotActOn()
    {
        // Arrange — the log is a record of what the provider said, not of what this module chose to
        // act on. Refusing an unsubscribed type would take every event somebody enables in the
        // Stripe dashboard to a 400, and Stripe into exponential backoff.
        string eventId = NewEventId();

        string payload =
            $$"""
              {
                "id": "{{eventId}}",
                "object": "event",
                "type": "customer.updated",
                "data": { "object": { "id": "cus_unmodelled", "object": "customer" } }
              }
              """;

        // Act
        HttpResponseMessage response = await PostWebhookAsync(payload);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        Result<bool> processed = await Poller.WaitAsync(ProjectionTimeout, () => ProcessedAsync(eventId));

        processed.IsSuccess.Should().BeTrue(
            "an event with no work attached is still finished — otherwise the outstanding rows fill " +
            "with traffic and stop meaning anything");
    }

    [Fact]
    public async Task Webhook_Should_RecordAnEventItCannotActOn_WithTheReason()
    {
        // Arrange — a setup_intent.succeeded naming a Stripe customer this service has never seen.
        // The card cannot be attached to anybody, and the interesting part is what is left behind:
        // neither outbox job retries (§5.6), so the unprocessed row carrying the reason is the ONLY
        // trace that this event still owes the platform some work.
        string eventId = NewEventId();
        string payload = SetupIntentSucceeded(eventId, "cus_nobody_here", "pm_orphan");

        // Act
        HttpResponseMessage response = await PostWebhookAsync(payload);

        // Assert — the endpoint still succeeds. The event was received and recorded; what failed is
        // the work, and telling Stripe to redeliver would not make the customer exist.
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        Result<string> reason = await Poller.WaitAsync(ProjectionTimeout, () => ErrorAsync(eventId));

        reason.IsSuccess.Should().BeTrue("the failure must be readable off the row");
        reason.Value.Should().Contain("cus_nobody_here");

        Result<bool> processed = await ProcessedAsync(eventId);

        processed.IsSuccess.Should().BeFalse(
            "an event whose work failed must keep reading as outstanding");
    }

    /// <summary>
    /// Stripe's signing scheme: <c>t={unix},v1={hex HMAC-SHA256 of "{t}.{payload}"}</c>. Written out
    /// rather than mocked, for the reason in the class remarks.
    /// </summary>
    private static string Sign(string payload, string secret = IntegrationTestWebAppFactory.WebhookSigningSecret)
    {
        long timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        byte[] digest = HMACSHA256.HashData(
            Encoding.UTF8.GetBytes(secret),
            Encoding.UTF8.GetBytes(string.Create(CultureInfo.InvariantCulture, $"{timestamp}.{payload}")));

        return string.Create(
            CultureInfo.InvariantCulture,
            $"t={timestamp},v1={Convert.ToHexStringLower(digest)}");
    }

    /// <summary>
    /// Posts the payload with no bearer token anywhere: the endpoint is anonymous, and the signature
    /// is the credential. Pass <see cref="string.Empty"/> for <paramref name="signature"/> to send
    /// no header at all.
    /// </summary>
    private async Task<HttpResponseMessage> PostWebhookAsync(string payload, string? signature = null)
    {
        HttpClient client = Factory.CreateClient();

        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(WebhookPath, UriKind.Relative))
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json")
        };

        signature ??= Sign(payload);

        if (signature.Length > 0)
        {
            request.Headers.Add(SignatureHeader, signature);
        }

        return await client.SendAsync(request, TestContext.Current.CancellationToken);
    }

    /// <summary>A fresh event id per test, so the unique index is never the reason a test fails.</summary>
    private static string NewEventId() => $"evt_{Guid.NewGuid():N}";

    private static string SetupIntentSucceeded(string eventId, string customerId, string paymentMethodId) =>
        $$"""
          {
            "id": "{{eventId}}",
            "object": "event",
            "type": "setup_intent.succeeded",
            "data": {
              "object": {
                "id": "seti_{{eventId}}",
                "object": "setup_intent",
                "status": "succeeded",
                "customer": "{{customerId}}",
                "payment_method": "{{paymentMethodId}}"
              }
            }
          }
          """;

    private async Task<string> WaitForStripeCustomerAsync(Guid customerId)
    {
        Result<string> result = await Poller.WaitAsync(ProjectionTimeout, async () =>
        {
            await using var connection = new NpgsqlConnection(Factory.ConnectionString);

            const string sql = "SELECT stripe_customer_id FROM customer_payment_profiles WHERE id = @CustomerId";

            string? found = await connection.QuerySingleOrDefaultAsync<string?>(
                sql,
                new { CustomerId = customerId });

            return string.IsNullOrEmpty(found)
                ? Result.Failure<string>(Error.NotFound("Test.ProfileMissing", "No payment profile yet"))
                : Result.Success(found);
        });

        result.IsSuccess.Should().BeTrue(
            "the payment profile for {0} is built from UserRegistered and every webhook test needs it",
            customerId);

        return result.Value;
    }

    private async Task<Result<string>> EventTypeAsync(string providerEventId)
    {
        await using var connection = new NpgsqlConnection(Factory.ConnectionString);

        const string sql = "SELECT event_type FROM stripe_event_logs WHERE provider_event_id = @ProviderEventId";

        string? found = await connection.QuerySingleOrDefaultAsync<string?>(
            sql,
            new { ProviderEventId = providerEventId });

        return string.IsNullOrEmpty(found)
            ? Result.Failure<string>(Error.NotFound("Test.EventMissing", "The event has not been recorded"))
            : Result.Success(found);
    }

    private async Task<Result<int>> EventCountAsync(string providerEventId)
    {
        int rows = await CountAsync(providerEventId);

        return rows > 0
            ? Result.Success(rows)
            : Result.Failure<int>(Error.NotFound("Test.EventMissing", "The event has not been recorded"));
    }

    private async Task<int> CountAsync(string providerEventId)
    {
        await using var connection = new NpgsqlConnection(Factory.ConnectionString);

        const string sql = "SELECT COUNT(*) FROM stripe_event_logs WHERE provider_event_id = @ProviderEventId";

        return await connection.ExecuteScalarAsync<int>(sql, new { ProviderEventId = providerEventId });
    }

    private async Task<Result<bool>> ProcessedAsync(string providerEventId)
    {
        await using var connection = new NpgsqlConnection(Factory.ConnectionString);

        const string sql =
            "SELECT processed_on_utc IS NOT NULL FROM stripe_event_logs WHERE provider_event_id = @ProviderEventId";

        bool? processed = await connection.QuerySingleOrDefaultAsync<bool?>(
            sql,
            new { ProviderEventId = providerEventId });

        return processed == true
            ? Result.Success(true)
            : Result.Failure<bool>(Error.NotFound("Test.NotProcessedYet", "The outbox has not caught up"));
    }

    private async Task<Result<string>> ErrorAsync(string providerEventId)
    {
        await using var connection = new NpgsqlConnection(Factory.ConnectionString);

        const string sql = "SELECT error FROM stripe_event_logs WHERE provider_event_id = @ProviderEventId";

        string? error = await connection.QuerySingleOrDefaultAsync<string?>(
            sql,
            new { ProviderEventId = providerEventId });

        return string.IsNullOrEmpty(error)
            ? Result.Failure<string>(Error.NotFound("Test.NoErrorYet", "The outbox has not caught up"))
            : Result.Success(error);
    }

    private async Task<Result<string>> SavedCardAsync(Guid customerId, string stripePaymentMethodId)
    {
        await using var connection = new NpgsqlConnection(Factory.ConnectionString);

        const string sql =
            "SELECT stripe_payment_method_id FROM customer_payment_profiles WHERE id = @CustomerId";

        string? saved = await connection.QuerySingleOrDefaultAsync<string?>(sql, new { CustomerId = customerId });

        return string.Equals(saved, stripePaymentMethodId, StringComparison.Ordinal)
            ? Result.Success(saved!)
            : Result.Failure<string>(Error.NotFound("Test.CardNotSavedYet", "The outbox has not caught up"));
    }
}

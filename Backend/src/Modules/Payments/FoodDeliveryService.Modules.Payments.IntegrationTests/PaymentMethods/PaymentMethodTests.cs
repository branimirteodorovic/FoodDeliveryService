using System.Net;
using System.Net.Http.Json;
using AwesomeAssertions;
using Dapper;
using FoodDeliveryService.Common.Domain;
using FoodDeliveryService.Modules.Payments.Application.Abstractions.Payments;
using FoodDeliveryService.Modules.Payments.Application.PaymentMethods.CreateSetupIntent;
using FoodDeliveryService.Modules.Payments.Application.PaymentMethods.GetPaymentMethods;
using FoodDeliveryService.Modules.Payments.IntegrationTests.Abstractions;
using FoodDeliveryService.Modules.Payments.Presentation.PaymentMethods;
using Npgsql;

namespace FoodDeliveryService.Modules.Payments.IntegrationTests.PaymentMethods;

/// <summary>
/// Saved payment methods, end to end — Feature 3.8 Milestone D, §6.
/// <para>
/// The whole flow is exercised through the real HTTP surface against real Postgres, Redis and
/// RabbitMQ, with the real Users host answering the permissions RPC. Only the Stripe seam is
/// substituted (§5.5), which is what lets "the gateway was called once, with this key" be an
/// assertion rather than a hope.
/// </para>
/// </summary>
public class PaymentMethodTests(IntegrationTestWebAppFactory factory) : BaseIntegrationTest(factory)
{
    private static readonly TimeSpan ProjectionTimeout = TimeSpan.FromSeconds(30);

    [Fact]
    public async Task Registration_Should_CreateTheStripeCustomer()
    {
        // Act — the profile is built from UserRegisteredIntegrationEvent, which the seeding in the
        // fixture raised: an outbox publish, a broker hop and an inbox dispatch ago (§6.2).
        Result<Guid> profile = await Poller.WaitAsync(
            ProjectionTimeout,
            () => ProfileExistsAsync(Factory.CustomerUserId));

        // Assert
        profile.IsSuccess.Should().BeTrue("every registered user gets a Stripe customer, eagerly");

        Factory.PaymentGateway
            .CallsFor(FakeGatewayOperation.CreateCustomer)
            .Should().Contain(call => call.IdempotencyKey == PaymentIdempotencyKeys.Customer(Factory.CustomerUserId));
    }

    [Fact]
    public async Task CreateSetupIntent_Should_ReturnAClientSecret()
    {
        // Arrange
        await WaitForProfileAsync(Factory.CustomerUserId);
        HttpClient client = await CreateCustomerClientAsync();

        // Act
        HttpResponseMessage response = await client.PostAsync(
            new Uri("payments/payment-methods/setup-intents", UriKind.Relative),
            content: null,
            TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        SetupIntentResponse? setupIntent =
            await response.Content.ReadFromJsonAsync<SetupIntentResponse>(TestContext.Current.CancellationToken);

        setupIntent!.SetupIntentId.Should().StartWith("seti_");
        setupIntent.ClientSecret.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task CreateSetupIntent_Should_MintAFreshKeyPerAttempt()
    {
        // Arrange — the one idempotency key in this feature deliberately not derived from a durable
        // id (PaymentIdempotencyKeys.SetupIntent). Replaying it would hand the second caller a
        // client secret Stripe.js has already consumed, so this pins that it does not.
        await WaitForProfileAsync(Factory.CustomerUserId);
        HttpClient client = await CreateCustomerClientAsync();

        // Act
        await client.PostAsync(
            new Uri("payments/payment-methods/setup-intents", UriKind.Relative),
            content: null,
            TestContext.Current.CancellationToken);

        await client.PostAsync(
            new Uri("payments/payment-methods/setup-intents", UriKind.Relative),
            content: null,
            TestContext.Current.CancellationToken);

        // Assert
        IReadOnlyList<FakeGatewayCall> calls =
            Factory.PaymentGateway.CallsFor(FakeGatewayOperation.CreateSetupIntent);

        calls.Should().HaveCountGreaterThanOrEqualTo(2);
        calls.Should().OnlyContain(call => !call.Replayed);
        calls.Select(call => call.IdempotencyKey).Distinct().Should().HaveSameCount(calls);
    }

    [Fact]
    public async Task AttachAndDetach_Should_RoundTripThroughTheApi()
    {
        // Arrange
        await WaitForProfileAsync(Factory.CustomerUserId);
        HttpClient client = await CreateCustomerClientAsync();

        // Act — attach through the Development-only test-card endpoint (§6.4); nothing can confirm a
        // SetupIntent in a browser yet.
        HttpResponseMessage attachResponse = await client.PostAsJsonAsync(
            "payments/payment-methods/test-cards",
            new AttachTestPaymentMethod.Request { StripePaymentMethodId = "pm_card_visa" },
            TestContext.Current.CancellationToken);

        attachResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var paymentMethodId =
            await attachResponse.Content.ReadFromJsonAsync<Guid>(TestContext.Current.CancellationToken);

        // Assert — the card is listed, with the display fields and without any Stripe identifier.
        HttpResponseMessage listResponse = await client.GetAsync(
            new Uri("payments/payment-methods", UriKind.Relative),
            TestContext.Current.CancellationToken);

        List<PaymentMethodResponse>? cards = await listResponse.Content
            .ReadFromJsonAsync<List<PaymentMethodResponse>>(TestContext.Current.CancellationToken);

        cards.Should().ContainSingle();
        cards![0].Id.Should().Be(paymentMethodId);
        cards[0].Brand.Should().Be("visa");
        cards[0].Last4.Should().Be("4242");

        // Act — and remove it again.
        HttpResponseMessage detachResponse = await client.DeleteAsync(
            new Uri($"payments/payment-methods/{paymentMethodId}", UriKind.Relative),
            TestContext.Current.CancellationToken);

        // Assert — the provider was told first (§6.3), and the list is empty afterwards.
        detachResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);

        Factory.PaymentGateway
            .CallsFor(FakeGatewayOperation.DetachPaymentMethod)
            .Should().Contain(call => call.IdempotencyKey == PaymentIdempotencyKeys
                .DetachPaymentMethod("pm_card_visa"));

        HttpResponseMessage emptyResponse = await client.GetAsync(
            new Uri("payments/payment-methods", UriKind.Relative),
            TestContext.Current.CancellationToken);

        List<PaymentMethodResponse>? none = await emptyResponse.Content
            .ReadFromJsonAsync<List<PaymentMethodResponse>>(TestContext.Current.CancellationToken);

        none.Should().BeEmpty();
    }

    [Fact]
    public async Task Detach_Should_Be404_ForSomebodyElsesCard()
    {
        // Arrange — the second customer saves a card; the first tries to remove it by id.
        await WaitForProfileAsync(Factory.OtherCustomerUserId);
        HttpClient owner = await CreateOtherCustomerClientAsync();

        HttpResponseMessage attachResponse = await owner.PostAsJsonAsync(
            "payments/payment-methods/test-cards",
            new AttachTestPaymentMethod.Request { StripePaymentMethodId = "pm_card_mastercard" },
            TestContext.Current.CancellationToken);

        attachResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var paymentMethodId =
            await attachResponse.Content.ReadFromJsonAsync<Guid>(TestContext.Current.CancellationToken);

        await WaitForProfileAsync(Factory.CustomerUserId);
        HttpClient stranger = await CreateCustomerClientAsync();

        // Act
        HttpResponseMessage response = await stranger.DeleteAsync(
            new Uri($"payments/payment-methods/{paymentMethodId}", UriKind.Relative),
            TestContext.Current.CancellationToken);

        // Assert — 404 rather than 403: the profile is read by the caller's own id, so the card is
        // not on the row that came back. There is no branch in which one customer reaches another's.
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task PaymentMethods_Should_Be403_WithoutThePermission()
    {
        // Arrange — a real RestaurantManager token. The 403 is the seeded permission set answering,
        // resolved over the real broker by the real Users consumer.
        HttpClient client = await CreateManagerClientAsync();

        // Act
        HttpResponseMessage response = await client.GetAsync(
            new Uri("payments/payment-methods", UriKind.Relative),
            TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task PaymentMethods_Should_Be401_WithoutAToken()
    {
        // Arrange
        HttpClient client = Factory.CreateClient();

        // Act
        HttpResponseMessage response = await client.GetAsync(
            new Uri("payments/payment-methods", UriKind.Relative),
            TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Attaching_Should_ProjectTheCardFlagOntoOrders()
    {
        // Arrange
        await WaitForProfileAsync(Factory.CustomerUserId);
        HttpClient client = await CreateCustomerClientAsync();

        // Act
        HttpResponseMessage attachResponse = await client.PostAsJsonAsync(
            "payments/payment-methods/test-cards",
            new AttachTestPaymentMethod.Request { StripePaymentMethodId = "pm_card_amex" },
            TestContext.Current.CancellationToken);

        attachResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var paymentMethodId =
            await attachResponse.Content.ReadFromJsonAsync<Guid>(TestContext.Current.CancellationToken);

        // Assert — asserted in Orders' own database, three hops away: the outbox publish, the broker,
        // and Orders' inbox. The half of a replication that goes wrong is the consuming half, and it
        // is invisible from here otherwise.
        Result<bool> attached = await Poller.WaitAsync(
            ProjectionTimeout,
            () => CardFlagAsync(Factory.CustomerUserId, expected: true));

        attached.IsSuccess.Should().BeTrue("Orders must learn that this customer can pay by card");

        // Act — and the flag must come back down again, or the replica only ever converges upwards.
        HttpResponseMessage detachResponse = await client.DeleteAsync(
            new Uri($"payments/payment-methods/{paymentMethodId}", UriKind.Relative),
            TestContext.Current.CancellationToken);

        detachResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // Assert
        Result<bool> cleared = await Poller.WaitAsync(
            ProjectionTimeout,
            () => CardFlagAsync(Factory.CustomerUserId, expected: false));

        cleared.IsSuccess.Should().BeTrue("a removed card must stop Orders offering card payment");
    }

    private async Task WaitForProfileAsync(Guid customerId)
    {
        Result<Guid> profile = await Poller.WaitAsync(ProjectionTimeout, () => ProfileExistsAsync(customerId));

        profile.IsSuccess.Should().BeTrue(
            "the payment profile for {0} is built from UserRegistered and every card test needs it",
            customerId);
    }

    private async Task<Result<Guid>> ProfileExistsAsync(Guid customerId)
    {
        await using var connection = new NpgsqlConnection(Factory.ConnectionString);

        const string sql = "SELECT id FROM customer_payment_profiles WHERE id = @CustomerId";

        Guid found = await connection.QuerySingleOrDefaultAsync<Guid>(sql, new { CustomerId = customerId });

        return found == Guid.Empty
            ? Result.Failure<Guid>(Error.NotFound("Test.ProfileMissing", "No payment profile yet"))
            : Result.Success(found);
    }

    private async Task<Result<bool>> CardFlagAsync(Guid customerId, bool expected)
    {
        await using var connection = new NpgsqlConnection(Factory.OrdersApi.ConnectionString);

        const string sql = "SELECT can_pay_by_card FROM customer_payment_profiles WHERE id = @CustomerId";

        bool? flag = await connection.QuerySingleOrDefaultAsync<bool?>(sql, new { CustomerId = customerId });

        return flag == expected
            ? Result.Success(expected)
            : Result.Failure<bool>(Error.NotFound("Test.FlagNotYet", "The replica has not caught up"));
    }

}

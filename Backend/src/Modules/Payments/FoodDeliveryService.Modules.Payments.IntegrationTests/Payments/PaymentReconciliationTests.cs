using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using AwesomeAssertions;
using Dapper;
using FoodDeliveryService.Common.Domain;
using FoodDeliveryService.Modules.Orders.IntegrationEvents;
using FoodDeliveryService.Modules.Payments.Domain.Payments;
using FoodDeliveryService.Modules.Payments.IntegrationTests.Abstractions;
using FoodDeliveryService.Modules.Payments.Presentation.PaymentMethods;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using OrdersDomain = FoodDeliveryService.Modules.Orders.Domain;
using OrdersUnitOfWork = FoodDeliveryService.Modules.Orders.Application.Abstractions.Data.IUnitOfWork;

namespace FoodDeliveryService.Modules.Payments.IntegrationTests.Payments;

/// <summary>
/// The webhook arms that finish a payment the API call could not — Feature 3.8 Milestone F (§8.1)
/// and Milestone G (§9).
/// <para>
/// <b>This is the milestone's load-bearing claim, tested against the case it exists for.</b> Neither
/// <c>ProcessOutboxJob</c> nor <c>ProcessInboxJob</c> retries (§5.6): a transient provider fault
/// writes an error onto the message row and the row is marked processed alongside the successes. So
/// a payment whose Stripe call went out and never came back is stranded, and the <em>only</em> thing
/// that moves it is Stripe's own account of what happened, arriving as a webhook. That makes §7 a
/// correctness requirement of §8 rather than a refinement of it, and this is the test that says so.
/// </para>
/// <para>
/// The setup scripts the gateway to throw, which is what <c>StripePaymentGateway</c> does for an
/// <c>api_connection_error</c> or an HTTP 5xx. The payment row survives because it is committed
/// <b>before</b> the provider is called — the ordering the aggregate's own remarks argue for, and
/// the reason the reconciliation has anything to find.
/// </para>
/// </summary>
public class PaymentReconciliationTests(IntegrationTestWebAppFactory factory) : BaseIntegrationTest(factory)
{
    private static readonly TimeSpan ProjectionTimeout = TimeSpan.FromSeconds(60);

    private const decimal UnitPrice = 12.50m;
    private const int Quantity = 2;

    /// <summary>Orders' <c>PaymentStatus.Authorized</c> and <c>.Failed</c>, as the column stores them.</summary>
    private const int OrdersPaymentAuthorized = 3;
    private const int OrdersPaymentFailed = 6;

    /// <summary>Orders' <c>PaymentStatus.Captured</c> — Milestone G.</summary>
    private const int OrdersPaymentCaptured = 4;

    [Fact]
    public async Task ACapturableWebhook_Should_FinishAPaymentWhoseCallNeverCameBack()
    {
        // Arrange — the gateway throws, exactly as it does for a provider outage. The payment row is
        // written first, so it survives in Authorizing with no provider id on it, and nothing
        // anywhere will try again. The script is held for the whole test: the authorization happens
        // on the inbox a second after the placement returns, so undoing it any earlier would undo it
        // before it was ever in force.
        using var faulting = new ScriptedGatewayOutcome(
            Factory.PaymentGateway,
            FakeGatewayOperation.Authorize,
            FakeGatewayOutcome.ThrowTransient);

        Guid orderId = await PlaceCardOrderAsync();

        Result<string> stranded = await Poller.WaitAsync(
            ProjectionTimeout,
            () => PaymentStatusAsync(orderId, PaymentStatus.Authorizing));

        stranded.IsSuccess.Should().BeTrue("the row must exist before the provider is called, or nothing can reconcile it");
        stranded.Value.Should().BeEmpty("the call never returned an intent to record");

        // Act — Stripe tells us what it did. The payload names a pi_… this platform has never seen,
        // so the only way back to the payment is the order_id this service wrote into the intent's
        // metadata and Stripe echoed back.
        string paymentIntentId = $"pi_test_reconciled_{Guid.NewGuid():N}"[..40];
        string eventId = StripeWebhooks.NewEventId();

        HttpResponseMessage response = await StripeWebhooks.PostAsync(
            Factory.CreateClient(),
            StripeWebhooks.AmountCapturableUpdated(eventId, paymentIntentId, orderId),
            signature: null,
            TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // Assert — the payment is authorized, against the intent the webhook named.
        Result<string> authorized = await Poller.WaitAsync(
            ProjectionTimeout,
            () => PaymentStatusAsync(orderId, PaymentStatus.Authorized));

        authorized.IsSuccess.Should().BeTrue("the webhook is the only thing that can finish this payment");
        authorized.Value.Should().Be(paymentIntentId);

        // And the order catches up too — the reconciliation is not a private repair inside Payments,
        // it re-drives the same event Orders was always going to hear.
        Result<int> projected = await Poller.WaitAsync(
            ProjectionTimeout,
            () => OrderPaymentStatusAsync(orderId, OrdersPaymentAuthorized));

        projected.IsSuccess.Should().BeTrue("the restaurant cannot accept the order until Orders knows");
    }

    [Fact]
    public async Task AFailedWebhook_Should_CancelAnOrderWhoseCallNeverCameBack()
    {
        // Arrange — the same stranding, with the opposite outcome at the provider.
        using var faulting = new ScriptedGatewayOutcome(
            Factory.PaymentGateway,
            FakeGatewayOperation.Authorize,
            FakeGatewayOutcome.ThrowTransient);

        Guid orderId = await PlaceCardOrderAsync();

        Result<string> stranded = await Poller.WaitAsync(
            ProjectionTimeout,
            () => PaymentStatusAsync(orderId, PaymentStatus.Authorizing));

        stranded.IsSuccess.Should().BeTrue();

        // Act — the decline code on the payload is the provider's; what gets stored is this
        // platform's bounded reason, mapped in the seam where every other Stripe translation happens.
        HttpResponseMessage response = await StripeWebhooks.PostAsync(
            Factory.CreateClient(),
            StripeWebhooks.PaymentFailed(
                StripeWebhooks.NewEventId(),
                $"pi_test_declined_{Guid.NewGuid():N}"[..38],
                orderId,
                declineCode: "insufficient_funds"),
            signature: null,
            TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // Assert
        Result<string> failed = await Poller.WaitAsync(ProjectionTimeout, () => PaymentFailureReasonAsync(orderId));

        failed.IsSuccess.Should().BeTrue("a refusal the platform never saw a response for is still a refusal");
        failed.Value.Should().Be(
            PaymentFailureReason.InsufficientFunds,
            "the issuer's decline code is mapped onto the bounded set, never forwarded raw");

        Result<int> cancelled = await Poller.WaitAsync(
            ProjectionTimeout,
            () => OrderPaymentStatusAsync(orderId, OrdersPaymentFailed));

        cancelled.IsSuccess.Should().BeTrue("the customer must not be left with an order nobody will ever charge for");
    }

    [Fact]
    public async Task ASucceededWebhook_Should_RecordACaptureThePlatformNeverSaw()
    {
        // Arrange — Milestone G's reconciling arm, and the two-step case it is built for: BOTH
        // provider responses were lost, so the row is still Authorizing while Stripe has a captured
        // intent. The arm records the hold and then the charge, so Orders converges through the
        // ordinary projections rather than jumping a state it never saw.
        using var faulting = new ScriptedGatewayOutcome(
            Factory.PaymentGateway,
            FakeGatewayOperation.Authorize,
            FakeGatewayOutcome.ThrowTransient);

        Guid orderId = await PlaceCardOrderAsync();

        Result<string> stranded = await Poller.WaitAsync(
            ProjectionTimeout,
            () => PaymentStatusAsync(orderId, PaymentStatus.Authorizing));

        stranded.IsSuccess.Should().BeTrue();

        // Act
        string paymentIntentId = $"pi_test_captured_{Guid.NewGuid():N}"[..40];

        HttpResponseMessage response = await StripeWebhooks.PostAsync(
            Factory.CreateClient(),
            StripeWebhooks.PaymentIntentSucceeded(StripeWebhooks.NewEventId(), paymentIntentId, orderId),
            signature: null,
            TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // Assert — captured, against the intent the webhook named, with no provider call of our own.
        Result<string> captured = await Poller.WaitAsync(
            ProjectionTimeout,
            () => PaymentStatusAsync(orderId, PaymentStatus.Captured));

        captured.IsSuccess.Should().BeTrue("the webhook is the only account of this capture the platform has");
        captured.Value.Should().Be(paymentIntentId);

        Factory.PaymentGateway
            .CallsFor(FakeGatewayOperation.Capture)
            .Should().NotContain(
                c => c.Subject == paymentIntentId,
                "the money has already moved; asking Stripe to capture it again is not reconciliation");

        // Orders catches up through both projections — Authorized first, then Captured.
        Result<int> projected = await Poller.WaitAsync(
            ProjectionTimeout,
            () => OrderPaymentStatusAsync(orderId, OrdersPaymentCaptured));

        projected.IsSuccess.Should().BeTrue("the order must end up agreeing that it was charged");
    }

    private async Task GiveTheCustomerACardAsync()
    {
        Result<Guid> profile = await Poller.WaitAsync(
            ProjectionTimeout,
            () => ProfileExistsAsync(Factory.CustomerUserId));

        profile.IsSuccess.Should().BeTrue();

        HttpClient client = await CreateCustomerClientAsync();

        HttpResponseMessage attached = await client.PostAsJsonAsync(
            "payments/payment-methods/test-cards",
            new AttachTestPaymentMethod.Request { StripePaymentMethodId = "pm_card_visa" },
            TestContext.Current.CancellationToken);

        attached.StatusCode.Should().Be(HttpStatusCode.OK);

        Result<bool> replicated = await Poller.WaitAsync(
            ProjectionTimeout,
            () => CardFlagAsync(Factory.CustomerUserId));

        replicated.IsSuccess.Should().BeTrue();
    }

    /// <summary>
    /// Gives the customer a card, seeds the menu the order is priced from, and places it. The card
    /// is attached inside this helper because its own gateway calls must NOT be the ones the caller
    /// scripted to fail — only the authorization is.
    /// </summary>
    private async Task<Guid> PlaceCardOrderAsync()
    {
        await GiveTheCustomerACardAsync();

        var restaurantId = Guid.NewGuid();
        var menuItemId = Guid.NewGuid();

        await SeedOrdersReplicasAsync(restaurantId, menuItemId);

        string accessToken = await GetAccessTokenAsync(Factory.CustomerUserEmail, Factory.TestUserPassword);

        HttpClient client = Factory.OrdersApi.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        var body = new
        {
            RestaurantId = restaurantId,
            Items = new[] { new { MenuItemId = menuItemId, Quantity } },
            DeliveryAddress = new
            {
                Street = Faker.Address.StreetAddress(),
                City = Faker.Address.City(),
                PostalCode = Faker.Address.ZipCode(),
                Country = Faker.Address.Country(),
                Notes = (string?)null,
                Latitude = Faker.Address.Latitude(),
                Longitude = Faker.Address.Longitude()
            },
            PaymentMethod = OrderPaymentMethods.Card
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, "orders")
        {
            Content = JsonContent.Create(body)
        };

        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());

        HttpResponseMessage response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        return await response.Content.ReadFromJsonAsync<Guid>(TestContext.Current.CancellationToken);
    }

    private async Task SeedOrdersReplicasAsync(Guid restaurantId, Guid menuItemId)
    {
        Result<Guid> customer = await Poller.WaitAsync(
            ProjectionTimeout,
            () => OrdersCustomerAsync(Factory.CustomerUserId));

        customer.IsSuccess.Should().BeTrue();

        await using AsyncServiceScope scope = Factory.OrdersApi.Services.CreateAsyncScope();

        var restaurants = scope.ServiceProvider
            .GetRequiredService<OrdersDomain.Restaurants.IRestaurantReplicaRepository>();
        var menuItems = scope.ServiceProvider
            .GetRequiredService<OrdersDomain.Restaurants.IMenuItemReplicaRepository>();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<OrdersUnitOfWork>();

        restaurants.Insert(OrdersDomain.Restaurants.Restaurant.Create(
            restaurantId,
            Guid.NewGuid(),
            Faker.Company.CompanyName(),
            0.15m,
            Faker.Address.Latitude(),
            Faker.Address.Longitude()));

        menuItems.Insert(OrdersDomain.Restaurants.MenuItem.Create(
            menuItemId,
            restaurantId,
            Faker.Commerce.ProductName(),
            UnitPrice,
            isAvailable: true));

        await unitOfWork.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// The payment's provider id at a given status, or a failure while it has not reached it. Empty
    /// rather than null when the payment exists with no intent recorded, which is the stranded state
    /// this class is about.
    /// </summary>
    private async Task<Result<string>> PaymentStatusAsync(Guid orderId, PaymentStatus expected)
    {
        await using var connection = new NpgsqlConnection(Factory.ConnectionString);

        const string sql =
            """
            SELECT COALESCE(stripe_payment_intent_id, '')
            FROM payments
            WHERE order_id = @OrderId AND status = @Status
            """;

        string? intentId = await connection.QuerySingleOrDefaultAsync<string?>(
            sql,
            new { OrderId = orderId, Status = (int)expected });

        return intentId is null
            ? Result.Failure<string>(Error.NotFound("Test.PaymentNotYet", "The payment has not reached that status"))
            : Result.Success(intentId);
    }

    private async Task<Result<string>> PaymentFailureReasonAsync(Guid orderId)
    {
        await using var connection = new NpgsqlConnection(Factory.ConnectionString);

        const string sql =
            """
            SELECT failure_reason
            FROM payments
            WHERE order_id = @OrderId AND status = @Status
            """;

        string? reason = await connection.QuerySingleOrDefaultAsync<string?>(
            sql,
            new { OrderId = orderId, Status = (int)PaymentStatus.Failed });

        return string.IsNullOrEmpty(reason)
            ? Result.Failure<string>(Error.NotFound("Test.FailureNotYet", "The payment has not failed"))
            : Result.Success(reason);
    }

    private async Task<Result<int>> OrderPaymentStatusAsync(Guid orderId, int expected)
    {
        await using var connection = new NpgsqlConnection(Factory.OrdersApi.ConnectionString);

        const string sql = "SELECT payment_status FROM orders WHERE id = @OrderId";

        int? status = await connection.QuerySingleOrDefaultAsync<int?>(sql, new { OrderId = orderId });

        return status == expected
            ? Result.Success(expected)
            : Result.Failure<int>(Error.NotFound("Test.OrderNotYet", "The order has not caught up"));
    }

    private async Task<Result<Guid>> OrdersCustomerAsync(Guid customerId)
    {
        await using var connection = new NpgsqlConnection(Factory.OrdersApi.ConnectionString);

        const string sql = "SELECT id FROM customers WHERE id = @CustomerId";

        Guid found = await connection.QuerySingleOrDefaultAsync<Guid>(sql, new { CustomerId = customerId });

        return found == Guid.Empty
            ? Result.Failure<Guid>(Error.NotFound("Test.CustomerNotYet", "Orders has not replicated the customer"))
            : Result.Success(found);
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

    private async Task<Result<bool>> CardFlagAsync(Guid customerId)
    {
        await using var connection = new NpgsqlConnection(Factory.OrdersApi.ConnectionString);

        const string sql = "SELECT can_pay_by_card FROM customer_payment_profiles WHERE id = @CustomerId";

        bool? flag = await connection.QuerySingleOrDefaultAsync<bool?>(sql, new { CustomerId = customerId });

        return flag == true
            ? Result.Success(true)
            : Result.Failure<bool>(Error.NotFound("Test.FlagNotYet", "The replica has not caught up"));
    }
}

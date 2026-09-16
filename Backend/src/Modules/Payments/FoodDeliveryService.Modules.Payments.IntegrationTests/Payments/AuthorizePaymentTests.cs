using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using AwesomeAssertions;
using Dapper;
using FoodDeliveryService.Common.Application.EventBus;
using FoodDeliveryService.Common.Domain;
using FoodDeliveryService.Modules.Orders.IntegrationEvents;
using FoodDeliveryService.Modules.Payments.Application.Abstractions.Payments;
using FoodDeliveryService.Modules.Payments.Domain.Payments;
using FoodDeliveryService.Modules.Payments.IntegrationTests.Abstractions;
using FoodDeliveryService.Modules.Payments.Presentation.PaymentMethods;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using OrdersDomain = FoodDeliveryService.Modules.Orders.Domain;
using OrdersUnitOfWork = FoodDeliveryService.Modules.Orders.Application.Abstractions.Data.IUnitOfWork;

namespace FoodDeliveryService.Modules.Payments.IntegrationTests.Payments;

/// <summary>
/// Authorize on placement, end to end — Feature 3.8 Milestone F, §8 and §13.2.
/// <para>
/// This is the first test in the suite that spans the whole saga rather than one service: a real
/// order is placed through the real Orders host, Orders publishes, Payments consumes and authorizes,
/// and the outcome travels back onto the order's payment dimension. Every assertion about the other
/// side is made in <em>that</em> service's own database, because the half of an event-driven flow
/// that goes wrong is the consuming half and it is invisible from the publisher.
/// </para>
/// <para>
/// Only the Stripe seam is substituted. That substitution is what makes the last test here possible
/// at all: a fake that records every call with its idempotency key can tell "the handler retried
/// harmlessly" from "the customer was charged twice", which the payment's final state cannot.
/// </para>
/// </summary>
public class AuthorizePaymentTests(IntegrationTestWebAppFactory factory) : BaseIntegrationTest(factory)
{
    private static readonly TimeSpan ProjectionTimeout = TimeSpan.FromSeconds(60);

    private const decimal UnitPrice = 12.50m;
    private const int Quantity = 2;
    private const decimal Subtotal = UnitPrice * Quantity;

    /// <summary>Orders' own <c>PaymentStatus</c> values, as the column stores them.</summary>
    private const int OrdersPaymentAuthorized = 3;
    private const int OrdersPaymentFailed = 6;

    /// <summary>Orders' <c>OrderStatus.Cancelled</c>.</summary>
    private const int OrdersStatusCancelled = 8;

    [Fact]
    public async Task ACardOrder_Should_BeAuthorizedAndProjectedBackOntoTheOrder()
    {
        // Arrange — the customer has a saved card, and Orders knows it. Without the replica the
        // placement is refused before any of this starts (§6.3).
        await GiveTheCustomerACardAsync("pm_card_visa");

        // Act
        Guid orderId = await PlaceOrderAsync(OrderPaymentMethods.Card);

        // Assert — the hold, in the Payments database.
        Result<string> authorized = await Poller.WaitAsync(
            ProjectionTimeout,
            () => PaymentStatusAsync(orderId, PaymentStatus.Authorized));

        authorized.IsSuccess.Should().BeTrue("a card order must end up holding funds");
        authorized.Value.Should().StartWith("pi_", "the provider's intent is recorded against the payment");

        // The call itself: one authorization, keyed on the order, for the order's own subtotal.
        FakeGatewayCall call = Factory.PaymentGateway
            .CallsFor(FakeGatewayOperation.Authorize)
            .Single(c => c.Subject == orderId.ToString());

        call.IdempotencyKey.Should().Be(PaymentIdempotencyKeys.Authorize(orderId));
        call.AmountMinorUnits.Should().Be(2500, "Subtotal is what is charged, in minor units (§0.4)");
        call.Replayed.Should().BeFalse();

        // And back on the order, two hops later — which is what lifts the guard on accepting it.
        Result<int> projected = await Poller.WaitAsync(
            ProjectionTimeout,
            () => OrderPaymentStatusAsync(orderId, OrdersPaymentAuthorized));

        projected.IsSuccess.Should().BeTrue("Orders must learn that the money is held");
    }

    [Fact]
    public async Task ADeclinedCard_Should_CancelTheOrder_WithItsOwnReason()
    {
        // Arrange — the 4000 0000 0000 9995 case, scripted rather than sent to Stripe.
        await GiveTheCustomerACardAsync("pm_card_visa_chargeDeclined");

        // Scoped to the whole test, not to the act: the authorization runs on the inbox a second
        // after the placement returns, so a script undone any earlier was never in force.
        using var declining = new ScriptedGatewayOutcome(
            Factory.PaymentGateway,
            FakeGatewayOperation.Authorize,
            FakeGatewayOutcome.Decline,
            PaymentFailureReason.InsufficientFunds);

        // Act
        Guid orderId = await PlaceOrderAsync(OrderPaymentMethods.Card);

        // Assert — the payment is failed and carries the bounded reason, never the provider's text.
        Result<string> failed = await Poller.WaitAsync(
            ProjectionTimeout,
            () => PaymentFailureReasonAsync(orderId));

        failed.IsSuccess.Should().BeTrue("a decline must be recorded, not dropped");
        failed.Value.Should().Be(PaymentFailureReason.InsufficientFunds);

        // And the order is cancelled, on its own path (§8.3) rather than through the customer's.
        Result<int> cancelled = await Poller.WaitAsync(
            ProjectionTimeout,
            () => OrderStatusAsync(orderId, OrdersStatusCancelled, OrdersPaymentFailed));

        cancelled.IsSuccess.Should().BeTrue("an order whose card was refused cannot go ahead");
    }

    [Fact]
    public async Task ACashOrder_Should_ReachPaymentsAndLeaveNothingBehind()
    {
        // Arrange — cash orders are skipped ENTIRELY (§8.1): no payment row, no provider call. The
        // event still arrives, which is why this asserts on the inbox first: "nothing happened"
        // means nothing after the message was processed, not nothing before it got here.
        await GiveTheCustomerACardAsync("pm_card_visa");

        // Act
        Guid orderId = await PlaceOrderAsync(OrderPaymentMethods.CashOnDelivery);

        Result<int> delivered = await Poller.WaitAsync(
            ProjectionTimeout,
            () => ProcessedPlacementsAsync(orderId, atLeast: 1));

        delivered.IsSuccess.Should().BeTrue("the placement must reach Payments before absence means anything");

        // Assert
        (await PaymentExistsAsync(orderId)).Should().BeFalse(
            "a cash order has nothing to authorize, and a row would put it in a state machine with no exit");

        Factory.PaymentGateway
            .CallsFor(FakeGatewayOperation.Authorize)
            .Should().NotContain(c => c.Subject == orderId.ToString());
    }

    [Fact]
    public async Task ARedeliveredPlacement_Should_NotAuthorizeTwice()
    {
        // The double-charge regression, and the one that matters (§13.2). Both async legs are
        // at-least-once, so the second delivery below is not a contrived scenario — it is the
        // contract. Two defences stand behind it: the handler's own "this order already has a
        // payment" read, and the idempotency key underneath, which would make the provider replay
        // the first response rather than take the money again.
        await GiveTheCustomerACardAsync("pm_card_visa");

        Guid orderId = await PlaceOrderAsync(OrderPaymentMethods.Card);

        Result<string> authorized = await Poller.WaitAsync(
            ProjectionTimeout,
            () => PaymentStatusAsync(orderId, PaymentStatus.Authorized));

        authorized.IsSuccess.Should().BeTrue();

        // Act — the same placement again. A fresh envelope so the inbox treats it as a new message
        // rather than deduplicating it before the handler runs: the point is to exercise the
        // handler, not the plumbing in front of it.
        await RepublishPlacementAsync(orderId);

        Result<int> both = await Poller.WaitAsync(
            ProjectionTimeout,
            () => ProcessedPlacementsAsync(orderId, atLeast: 2));

        both.IsSuccess.Should().BeTrue("the redelivery must actually have been processed");

        // Assert — exactly one authorization reached the gateway. Not "one effective charge": one
        // call. A replayed call would be recorded too, and would still be a defect here.
        Factory.PaymentGateway
            .CallsFor(FakeGatewayOperation.Authorize)
            .Where(c => c.Subject == orderId.ToString())
            .Should().ContainSingle("a redelivered placement must not reach the card a second time");
    }

    // ---- driving the other two services --------------------------------------------------------

    /// <summary>
    /// Attaches a card through the Development-only endpoint (§6.4) and waits for Orders' one-flag
    /// replica to catch up — the placement is refused until it has.
    /// </summary>
    private async Task GiveTheCustomerACardAsync(string stripePaymentMethodId)
    {
        Result<Guid> profile = await Poller.WaitAsync(
            ProjectionTimeout,
            () => ProfileExistsAsync(Factory.CustomerUserId));

        profile.IsSuccess.Should().BeTrue("the Stripe customer is created from UserRegistered (§6.2)");

        HttpClient client = await CreateCustomerClientAsync();

        HttpResponseMessage attached = await client.PostAsJsonAsync(
            "payments/payment-methods/test-cards",
            new AttachTestPaymentMethod.Request { StripePaymentMethodId = stripePaymentMethodId },
            TestContext.Current.CancellationToken);

        attached.StatusCode.Should().Be(HttpStatusCode.OK);

        Result<bool> replicated = await Poller.WaitAsync(
            ProjectionTimeout,
            () => CardFlagAsync(Factory.CustomerUserId));

        replicated.IsSuccess.Should().BeTrue("Orders refuses a card order until it knows the card exists");
    }

    /// <summary>
    /// Places a real order through the real <c>POST orders</c> endpoint on the in-process Orders
    /// host, after seeding the replicas its pricing reads. Nothing here is a shortcut: the order's
    /// subtotal is computed server-side from the menu replica, which is what makes the amount this
    /// suite asserts on the same number production would charge.
    /// </summary>
    private async Task<Guid> PlaceOrderAsync(string paymentMethod)
    {
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
            PaymentMethod = paymentMethod
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, "orders")
        {
            Content = JsonContent.Create(body)
        };

        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());

        HttpResponseMessage response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK, "the order must be placed for anything else to happen");

        return await response.Content.ReadFromJsonAsync<Guid>(TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// A restaurant and one available menu item in the Orders database. The customer replica is not
    /// seeded here — it arrives from <c>UserRegisteredIntegrationEvent</c> like everything else, and
    /// waiting for it is the honest thing to do because a placement is refused without it.
    /// </summary>
    private async Task SeedOrdersReplicasAsync(Guid restaurantId, Guid menuItemId)
    {
        Result<Guid> customer = await Poller.WaitAsync(
            ProjectionTimeout,
            () => OrdersCustomerAsync(Factory.CustomerUserId));

        customer.IsSuccess.Should().BeTrue("Orders replicates the customer from UserRegistered");

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
    /// Publishes a second copy of the placement, from the Orders host's own bus — the same route a
    /// real redelivery takes. A fresh envelope id, because deduplicating in the inbox would prove
    /// the plumbing works and say nothing about the handler.
    /// </summary>
    private async Task RepublishPlacementAsync(Guid orderId)
    {
        await using AsyncServiceScope scope = Factory.OrdersApi.Services.CreateAsyncScope();

        var eventBus = scope.ServiceProvider.GetRequiredService<IEventBus>();

        await eventBus.PublishAsync(
            new OrderPlacedIntegrationEvent(
                Guid.NewGuid(),
                DateTime.UtcNow,
                orderId,
                Factory.CustomerUserId,
                Guid.NewGuid(),
                Subtotal,
                OrderPaymentMethods.Card,
                DateTime.UtcNow),
            TestContext.Current.CancellationToken);
    }

    // ---- reading both databases ----------------------------------------------------------------

    private async Task<Result<string>> PaymentStatusAsync(Guid orderId, PaymentStatus expected)
    {
        await using var connection = new NpgsqlConnection(Factory.ConnectionString);

        const string sql =
            """
            SELECT stripe_payment_intent_id
            FROM payments
            WHERE order_id = @OrderId AND status = @Status
            """;

        string? intentId = await connection.QuerySingleOrDefaultAsync<string?>(
            sql,
            new { OrderId = orderId, Status = (int)expected });

        return string.IsNullOrEmpty(intentId)
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

    private async Task<bool> PaymentExistsAsync(Guid orderId)
    {
        await using var connection = new NpgsqlConnection(Factory.ConnectionString);

        const string sql = "SELECT COUNT(*) FROM payments WHERE order_id = @OrderId";

        return await connection.ExecuteScalarAsync<long>(sql, new { OrderId = orderId }) > 0;
    }

    /// <summary>
    /// How many copies of one order's placement the Payments inbox has finished with. It is what
    /// makes both "nothing happened" assertions in this class deterministic rather than a race
    /// against the Quartz tick.
    /// </summary>
    private async Task<Result<int>> ProcessedPlacementsAsync(Guid orderId, int atLeast)
    {
        await using var connection = new NpgsqlConnection(Factory.ConnectionString);

        const string sql =
            """
            SELECT COUNT(*)
            FROM inbox_messages
            WHERE type LIKE '%OrderPlacedIntegrationEvent%'
              AND content::text LIKE @OrderPattern
              AND processed_on_utc IS NOT NULL
            """;

        long processed = await connection.ExecuteScalarAsync<long>(
            sql,
            new { OrderPattern = $"%{orderId}%" });

        return processed >= atLeast
            ? Result.Success((int)processed)
            : Result.Failure<int>(Error.NotFound("Test.InboxNotYet", "The placement has not been processed yet"));
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

    private async Task<Result<int>> OrderStatusAsync(Guid orderId, int expectedStatus, int expectedPaymentStatus)
    {
        await using var connection = new NpgsqlConnection(Factory.OrdersApi.ConnectionString);

        const string sql =
            """
            SELECT COUNT(*)
            FROM orders
            WHERE id = @OrderId AND status = @Status AND payment_status = @PaymentStatus
            """;

        long matched = await connection.ExecuteScalarAsync<long>(
            sql,
            new { OrderId = orderId, Status = expectedStatus, PaymentStatus = expectedPaymentStatus });

        return matched > 0
            ? Result.Success(expectedStatus)
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

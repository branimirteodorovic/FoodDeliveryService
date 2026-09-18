using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using AwesomeAssertions;
using Dapper;
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
/// Capture and release, end to end — Feature 3.8 Milestone G, §9.
/// <para>
/// The second half of the loop <c>AuthorizePaymentTests</c> opens. Every transition here is driven
/// through a <b>real endpoint on the real Orders host</b> — the restaurant accepts, rejects, or the
/// customer cancels — and asserted in the Payments database and then back in the Orders one. Nothing
/// publishes a lifecycle event by hand: the thing most likely to be wrong in this milestone is a
/// missing consumer registration, and a hand-published event would hide exactly that.
/// </para>
/// <para>
/// The restaurant replica is seeded under the seeded manager's own user id, so the ownership guard
/// on accept and reject is satisfied the way it is in production rather than bypassed.
/// </para>
/// </summary>
public class CapturePaymentTests(IntegrationTestWebAppFactory factory) : BaseIntegrationTest(factory)
{
    private static readonly TimeSpan ProjectionTimeout = TimeSpan.FromSeconds(60);

    private const decimal UnitPrice = 12.50m;
    private const int Quantity = 2;

    /// <summary>Orders' own <c>PaymentStatus</c> values, as the column stores them.</summary>
    private const int OrdersPaymentCaptured = 4;
    private const int OrdersPaymentReleased = 5;

    [Fact]
    public async Task AnAcceptedOrder_Should_CaptureTheHold_AndProjectBackOntoTheOrder()
    {
        // Arrange
        Guid orderId = await PlaceAuthorizedCardOrderAsync();
        string intentId = await IntentIdAsync(orderId);

        // Act — the restaurant takes the order on. This is also the guard from §8.2 being satisfied
        // for real: the accept would have been refused had the authorization not been projected back.
        await AcceptAsync(orderId);

        // Assert — the money moved, in the Payments database.
        Result<int> captured = await Poller.WaitAsync(
            ProjectionTimeout,
            () => PaymentStatusAsync(orderId, PaymentStatus.Captured));

        captured.IsSuccess.Should().BeTrue("accepting a card order is what takes the money");

        // One capture call, keyed on the order, against the intent the authorization created.
        FakeGatewayCall call = Factory.PaymentGateway
            .CallsFor(FakeGatewayOperation.Capture)
            .Single(c => c.Subject == intentId);

        call.IdempotencyKey.Should().Be(PaymentIdempotencyKeys.Capture(orderId));
        call.Replayed.Should().BeFalse();

        // And back onto the order — which drives nothing there, and is the only way anyone asks
        // "was this charged?" without a synchronous call to Payments.
        Result<int> projected = await Poller.WaitAsync(
            ProjectionTimeout,
            () => OrderPaymentStatusAsync(orderId, OrdersPaymentCaptured));

        projected.IsSuccess.Should().BeTrue("Orders must learn that the money was taken");
    }

    [Fact]
    public async Task ARejectedOrder_Should_ReleaseTheHold_Uncharged()
    {
        // Arrange
        Guid orderId = await PlaceAuthorizedCardOrderAsync();
        string intentId = await IntentIdAsync(orderId);

        // Act — the restaurant refuses it.
        await RejectAsync(orderId);

        // Assert
        Result<int> released = await Poller.WaitAsync(
            ProjectionTimeout,
            () => PaymentStatusAsync(orderId, PaymentStatus.Released));

        released.IsSuccess.Should().BeTrue("a hold that will never be captured must be given up");

        FakeGatewayCall call = Factory.PaymentGateway
            .CallsFor(FakeGatewayOperation.Release)
            .Single(c => c.Subject == intentId);

        call.IdempotencyKey.Should().Be(PaymentIdempotencyKeys.Release(orderId));

        // Nothing was ever charged, and the distinction matters to a customer: this is Released, not
        // Failed and not Refunded.
        Factory.PaymentGateway
            .CallsFor(FakeGatewayOperation.Capture)
            .Should().NotContain(c => c.Subject == intentId);

        Result<int> projected = await Poller.WaitAsync(
            ProjectionTimeout,
            () => OrderPaymentStatusAsync(orderId, OrdersPaymentReleased));

        projected.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task ACancelledOrder_Should_ReleaseTheHold()
    {
        // Arrange — the customer's own ending rather than the restaurant's. Same instruction to the
        // money, and it arrives on a different event through a different consumer, which is the part
        // worth testing separately.
        Guid orderId = await PlaceAuthorizedCardOrderAsync();
        string intentId = await IntentIdAsync(orderId);

        // Act — the customer's client against the ORDERS host. The base class's customer client
        // talks to the Payments host, where `orders/{id}/cancel` is not a route and the call comes
        // back 404 rather than cancelling anything, which is what this test did until the suite was
        // first executed against a real Docker (§10.5).
        HttpClient customer = await CreateOrdersCustomerClientAsync();

        HttpResponseMessage cancelled = await customer.PostAsync(
            new Uri($"orders/{orderId}/cancel", UriKind.Relative),
            content: null,
            TestContext.Current.CancellationToken);

        cancelled.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // Assert
        Result<int> released = await Poller.WaitAsync(
            ProjectionTimeout,
            () => PaymentStatusAsync(orderId, PaymentStatus.Released));

        released.IsSuccess.Should().BeTrue();

        Factory.PaymentGateway
            .CallsFor(FakeGatewayOperation.Release)
            .Should().ContainSingle(c => c.Subject == intentId);
    }

    [Fact]
    public async Task ARedeliveredAcceptance_Should_NotCaptureTwice()
    {
        // The double-charge regression for §9 — the same shape as the one §13.2 names for §8, and
        // the same reason: both async legs are at-least-once, so a second delivery of the acceptance
        // is the contract rather than a contrived case. Two defences stand behind it, the aggregate's
        // terminal-state no-op and the idempotency key underneath it.
        Guid orderId = await PlaceAuthorizedCardOrderAsync();
        string intentId = await IntentIdAsync(orderId);

        await AcceptAsync(orderId);

        Result<int> captured = await Poller.WaitAsync(
            ProjectionTimeout,
            () => PaymentStatusAsync(orderId, PaymentStatus.Captured));

        captured.IsSuccess.Should().BeTrue();

        // Act — the same acceptance again, from the Orders host's own bus, with a fresh envelope so
        // the inbox does not deduplicate it before the handler ever runs.
        await RepublishAcceptanceAsync(orderId);

        Result<int> both = await Poller.WaitAsync(
            ProjectionTimeout,
            () => ProcessedAcceptancesAsync(orderId, atLeast: 2));

        both.IsSuccess.Should().BeTrue("the redelivery must actually have been processed");

        // Assert — one call, not "one effective charge". A replayed call would be recorded too, and
        // would still be a defect: it means the handler asked the provider to take the money twice.
        Factory.PaymentGateway
            .CallsFor(FakeGatewayOperation.Capture)
            .Where(c => c.Subject == intentId)
            .Should().ContainSingle("a redelivered acceptance must not reach the card a second time");
    }

    [Fact]
    public async Task AnAcceptedCashOrder_Should_LeaveTheGatewayAlone()
    {
        // A cash order has no payment row, and this event carries no payment method to filter on
        // (§9.1) — absence is the whole mechanism, so it is worth an assertion of its own.
        Guid orderId = await PlaceOrderAsync(OrderPaymentMethods.CashOnDelivery);

        await AcceptAsync(orderId);

        Result<int> processed = await Poller.WaitAsync(
            ProjectionTimeout,
            () => ProcessedAcceptancesAsync(orderId, atLeast: 1));

        processed.IsSuccess.Should().BeTrue("the acceptance must reach Payments before absence means anything");

        (await PaymentExistsAsync(orderId)).Should().BeFalse();

        Factory.PaymentGateway
            .CallsFor(FakeGatewayOperation.Capture)
            .Should().NotContain(c => c.IdempotencyKey == PaymentIdempotencyKeys.Capture(orderId));
    }

    // ---- driving the other two services --------------------------------------------------------

    /// <summary>A card order that has reached <see cref="PaymentStatus.Authorized"/> — §9's starting point.</summary>
    private async Task<Guid> PlaceAuthorizedCardOrderAsync()
    {
        await GiveTheCustomerACardAsync();

        Guid orderId = await PlaceOrderAsync(OrderPaymentMethods.Card);

        Result<int> authorized = await Poller.WaitAsync(
            ProjectionTimeout,
            () => PaymentStatusAsync(orderId, PaymentStatus.Authorized));

        authorized.IsSuccess.Should().BeTrue("there is nothing to capture or release until the hold exists");

        // Orders must have projected it too, or Order.Accept() refuses (§8.2).
        Result<int> projected = await Poller.WaitAsync(
            ProjectionTimeout,
            () => OrderPaymentStatusAsync(orderId, expected: 3));

        projected.IsSuccess.Should().BeTrue();

        return orderId;
    }

    private async Task AcceptAsync(Guid orderId)
    {
        HttpClient manager = await CreateOrdersManagerClientAsync();

        HttpResponseMessage accepted = await manager.PostAsync(
            new Uri($"orders/{orderId}/accept", UriKind.Relative),
            content: null,
            TestContext.Current.CancellationToken);

        accepted.StatusCode.Should().Be(HttpStatusCode.NoContent, "the acceptance is what drives the capture");
    }

    private async Task RejectAsync(Guid orderId)
    {
        HttpClient manager = await CreateOrdersManagerClientAsync();

        HttpResponseMessage rejected = await manager.PostAsJsonAsync(
            $"orders/{orderId}/reject",
            new { Reason = "The kitchen is closed" },
            TestContext.Current.CancellationToken);

        rejected.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    /// <summary>
    /// The manager's client against the <b>Orders</b> host — the base class's manager client talks
    /// to the Payments host, where that user deliberately holds nothing.
    /// </summary>
    private async Task<HttpClient> CreateOrdersManagerClientAsync()
    {
        string accessToken = await GetAccessTokenAsync(Factory.ManagerUserEmail, Factory.TestUserPassword);

        HttpClient client = Factory.OrdersApi.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        return client;
    }

    /// <summary>
    /// The customer's client against the <b>Orders</b> host, for the same reason: every base-class
    /// client is bound to the Payments host, and an order endpoint called there answers 404.
    /// </summary>
    private async Task<HttpClient> CreateOrdersCustomerClientAsync()
    {
        string accessToken = await GetAccessTokenAsync(Factory.CustomerUserEmail, Factory.TestUserPassword);

        HttpClient client = Factory.OrdersApi.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        return client;
    }

    private async Task GiveTheCustomerACardAsync()
    {
        Result<Guid> profile = await Poller.WaitAsync(
            ProjectionTimeout,
            () => ProfileExistsAsync(Factory.CustomerUserId));

        profile.IsSuccess.Should().BeTrue("the Stripe customer is created from UserRegistered (§6.2)");

        HttpClient client = await CreateCustomerClientAsync();

        HttpResponseMessage attached = await client.PostAsJsonAsync(
            "payments/payment-methods/test-cards",
            new AttachTestPaymentMethod.Request { StripePaymentMethodId = "pm_card_visa" },
            TestContext.Current.CancellationToken);

        attached.StatusCode.Should().Be(HttpStatusCode.OK);

        Result<bool> replicated = await Poller.WaitAsync(
            ProjectionTimeout,
            () => CardFlagAsync(Factory.CustomerUserId));

        replicated.IsSuccess.Should().BeTrue("Orders refuses a card order until it knows the card exists");
    }

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

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        return await response.Content.ReadFromJsonAsync<Guid>(TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// A restaurant owned by the seeded manager, and one available menu item. The ownership is the
    /// difference from <c>AuthorizePaymentTests</c>'s copy: these tests accept and reject, and the
    /// guard in <c>OrderOwnership</c> answers a non-owner with a 404.
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
            Factory.ManagerUserId,
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

    /// <summary>A second copy of the acceptance, published from the Orders host's own bus.</summary>
    private async Task RepublishAcceptanceAsync(Guid orderId)
    {
        await using AsyncServiceScope scope = Factory.OrdersApi.Services.CreateAsyncScope();

        var eventBus = scope.ServiceProvider
            .GetRequiredService<Common.Application.EventBus.IEventBus>();

        await eventBus.PublishAsync(
            new OrderAcceptedIntegrationEvent(
                Guid.NewGuid(),
                DateTime.UtcNow,
                orderId,
                Factory.CustomerUserId,
                Guid.NewGuid(),
                DateTime.UtcNow),
            TestContext.Current.CancellationToken);
    }

    // ---- reading both databases ----------------------------------------------------------------

    private async Task<Result<int>> PaymentStatusAsync(Guid orderId, PaymentStatus expected)
    {
        await using var connection = new NpgsqlConnection(Factory.ConnectionString);

        const string sql = "SELECT COUNT(*) FROM payments WHERE order_id = @OrderId AND status = @Status";

        long matched = await connection.ExecuteScalarAsync<long>(
            sql,
            new { OrderId = orderId, Status = (int)expected });

        return matched > 0
            ? Result.Success((int)expected)
            : Result.Failure<int>(Error.NotFound("Test.PaymentNotYet", "The payment has not reached that status"));
    }

    private async Task<string> IntentIdAsync(Guid orderId)
    {
        await using var connection = new NpgsqlConnection(Factory.ConnectionString);

        const string sql = "SELECT stripe_payment_intent_id FROM payments WHERE order_id = @OrderId";

        string? intentId = await connection.QuerySingleOrDefaultAsync<string?>(sql, new { OrderId = orderId });

        intentId.Should().NotBeNullOrEmpty("the hold is held against a provider intent");

        return intentId!;
    }

    private async Task<bool> PaymentExistsAsync(Guid orderId)
    {
        await using var connection = new NpgsqlConnection(Factory.ConnectionString);

        const string sql = "SELECT COUNT(*) FROM payments WHERE order_id = @OrderId";

        return await connection.ExecuteScalarAsync<long>(sql, new { OrderId = orderId }) > 0;
    }

    /// <summary>
    /// How many copies of one order's acceptance the Payments inbox has finished with — what makes
    /// the redelivery and the cash-order assertions deterministic rather than a race against Quartz.
    /// Note <c>content::text</c>: the column is jsonb, and <c>LIKE</c> has no jsonb overload.
    /// </summary>
    private async Task<Result<int>> ProcessedAcceptancesAsync(Guid orderId, int atLeast)
    {
        await using var connection = new NpgsqlConnection(Factory.ConnectionString);

        const string sql =
            """
            SELECT COUNT(*)
            FROM inbox_messages
            WHERE type LIKE '%OrderAcceptedIntegrationEvent%'
              AND content::text LIKE @OrderPattern
              AND processed_on_utc IS NOT NULL
            """;

        long processed = await connection.ExecuteScalarAsync<long>(
            sql,
            new { OrderPattern = $"%{orderId}%" });

        return processed >= atLeast
            ? Result.Success((int)processed)
            : Result.Failure<int>(Error.NotFound("Test.InboxNotYet", "The acceptance has not been processed yet"));
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

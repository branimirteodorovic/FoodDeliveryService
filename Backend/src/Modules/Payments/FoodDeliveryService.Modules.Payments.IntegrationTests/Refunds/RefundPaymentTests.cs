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
using FoodDeliveryService.Modules.Payments.Domain.Refunds;
using FoodDeliveryService.Modules.Payments.IntegrationTests.Abstractions;
using FoodDeliveryService.Modules.Payments.Presentation.PaymentMethods;
using FoodDeliveryService.Modules.Support.IntegrationEvents;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using OrdersDomain = FoodDeliveryService.Modules.Orders.Domain;
using OrdersUnitOfWork = FoodDeliveryService.Modules.Orders.Application.Abstractions.Data.IUnitOfWork;

namespace FoodDeliveryService.Modules.Payments.IntegrationTests.Refunds;

/// <summary>
/// Real refunds, end to end — Feature 3.8 Milestone H, §10.1.
/// <para>
/// <b>The approval is published by hand here, unlike every lifecycle event in
/// <c>CapturePaymentTests</c>.</b> That suite drives accept and reject through real endpoints on a
/// real Orders host precisely so a missing consumer registration cannot hide; the same trick is not
/// available for Support, which this harness does not host. Publishing the contract onto the bus
/// still exercises the registration, the consumer, the inbox and the handler — everything except
/// Support's own decision path, which <c>Support.UnitTests</c> and <c>Support.IntegrationTests</c>
/// cover from the other side.
/// </para>
/// <para>
/// The refusals get as much room as the happy path, and deliberately. Three of the four ways a
/// refund does not happen never reach the provider at all, so the only evidence they produce is the
/// event this service publishes — and an approved refund that goes quiet is the failure this
/// milestone exists to remove.
/// </para>
/// </summary>
public class RefundPaymentTests(IntegrationTestWebAppFactory factory) : BaseIntegrationTest(factory)
{
    private static readonly TimeSpan ProjectionTimeout = TimeSpan.FromSeconds(60);

    private const decimal UnitPrice = 12.50m;
    private const int Quantity = 2;

    /// <summary>The order subtotal every test here works from: 2 × 12.50.</summary>
    private const decimal Subtotal = 25.00m;

    private const string TicketReference = "SUP-00004242";

    [Fact]
    public async Task AnApprovedRefund_Should_ReturnTheMoney_AndSettle()
    {
        // Arrange — money actually taken, which is the only state a refund is legal from.
        Guid orderId = await PlaceCapturedCardOrderAsync();
        string intentId = await IntentIdAsync(orderId);
        var refundRequestId = Guid.NewGuid();

        // Act
        await ApproveRefundAsync(refundRequestId, orderId, Subtotal);

        // Assert — the refund row settled, against a provider refund.
        Result<int> settled = await Poller.WaitAsync(
            ProjectionTimeout,
            () => RefundStatusAsync(refundRequestId, RefundStatus.Settled));

        settled.IsSuccess.Should().BeTrue("an approved refund on a captured payment must move money");

        FakeGatewayCall call = Factory.PaymentGateway
            .CallsFor(FakeGatewayOperation.Refund)
            .Single(c => c.Subject == intentId);

        // Keyed on Support's request id — the durable id, and the one thing a redelivery cannot
        // change (rule 1 of §1.4).
        call.IdempotencyKey.Should().Be(PaymentIdempotencyKeys.Refund(refundRequestId));
        call.AmountMinorUnits.Should().Be(2500);
        call.Replayed.Should().BeFalse();

        // And the payment itself: fully refunded, so terminal in the one state reachable from
        // Captured.
        Result<int> refunded = await Poller.WaitAsync(
            ProjectionTimeout,
            () => PaymentStatusAsync(orderId, PaymentStatus.Refunded));

        refunded.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task APartialRefund_Should_LeaveThePaymentCaptured()
    {
        // Arrange
        Guid orderId = await PlaceCapturedCardOrderAsync();
        var refundRequestId = Guid.NewGuid();

        // Act — 10 of the 25.
        await ApproveRefundAsync(refundRequestId, orderId, 10.00m);

        // Assert
        Result<int> settled = await Poller.WaitAsync(
            ProjectionTimeout,
            () => RefundStatusAsync(refundRequestId, RefundStatus.Settled));

        settled.IsSuccess.Should().BeTrue();

        // Still Captured: most of the money is still with the business, and the status is about
        // where the money is rather than about how many refunds have touched it.
        (await PaymentStatusValueAsync(orderId)).Should().Be((int)PaymentStatus.Captured);
    }

    [Fact]
    public async Task ARefundAboveWhatWasCaptured_Should_FailWithoutCallingTheProvider()
    {
        // Arrange — Support caps a request at the replicated order subtotal, so this one passed that
        // check when it was raised. The captured amount is the number it cannot see, and here they
        // are the same figure only because the whole order was captured.
        Guid orderId = await PlaceCapturedCardOrderAsync();
        string intentId = await IntentIdAsync(orderId);
        var refundRequestId = Guid.NewGuid();

        // Act — a cent over what was taken.
        await ApproveRefundAsync(refundRequestId, orderId, Subtotal + 0.01m);

        // Assert
        Result<int> failed = await Poller.WaitAsync(
            ProjectionTimeout,
            () => RefundStatusAsync(refundRequestId, RefundStatus.Failed));

        failed.IsSuccess.Should().BeTrue();

        (await RefundFailureReasonAsync(refundRequestId))
            .Should().Be(RefundFailureReason.AmountExceedsCaptured);

        // Refused before the provider was asked. Stripe would have refused it too, but a refusal
        // this platform can explain beats one it has to translate.
        Factory.PaymentGateway
            .CallsFor(FakeGatewayOperation.Refund)
            .Should().NotContain(c => c.Subject == intentId);
    }

    [Fact]
    public async Task ARefundOnAnUncapturedPayment_Should_Fail()
    {
        // Arrange — authorized and no further. The hold is there; the money is not, so there is
        // nothing to give back and releasing a hold is not a refund.
        Guid orderId = await PlaceAuthorizedCardOrderAsync();
        var refundRequestId = Guid.NewGuid();

        // Act
        await ApproveRefundAsync(refundRequestId, orderId, 5.00m);

        // Assert
        Result<int> failed = await Poller.WaitAsync(
            ProjectionTimeout,
            () => RefundStatusAsync(refundRequestId, RefundStatus.Failed));

        failed.IsSuccess.Should().BeTrue();

        (await RefundFailureReasonAsync(refundRequestId))
            .Should().Be(RefundFailureReason.PaymentNotCaptured);
    }

    [Fact]
    public async Task ARefundOnACashOrder_Should_FailRatherThanGoQuiet()
    {
        // Arrange — Support lets a refund be requested on any order, cash included: its ceiling is
        // the replicated subtotal, which a cash order has like any other. There is no Payment row to
        // act on, and the whole point of this case is that absence produces an answer.
        Guid orderId = await PlaceOrderAsync(OrderPaymentMethods.CashOnDelivery);
        var refundRequestId = Guid.NewGuid();

        // Act
        await ApproveRefundAsync(refundRequestId, orderId, 5.00m);

        // Assert
        Result<int> failed = await Poller.WaitAsync(
            ProjectionTimeout,
            () => RefundStatusAsync(refundRequestId, RefundStatus.Failed));

        failed.IsSuccess.Should().BeTrue(
            "an approved refund that this service cannot pay must say so, not return success silently");

        (await RefundFailureReasonAsync(refundRequestId))
            .Should().Be(RefundFailureReason.NotCardPayment);

        Factory.PaymentGateway
            .CallsFor(FakeGatewayOperation.Refund)
            .Should().NotContain(c => c.IdempotencyKey == PaymentIdempotencyKeys.Refund(refundRequestId));
    }

    [Fact]
    public async Task ARedeliveredApproval_Should_NotRefundTwice()
    {
        // The double-refund regression, the mirror of the double-charge one in CapturePaymentTests
        // and the test that matters most in this milestone: the inbox is at-least-once, so a second
        // delivery of one approval is the contract rather than a contrived case.
        Guid orderId = await PlaceCapturedCardOrderAsync();
        string intentId = await IntentIdAsync(orderId);
        var refundRequestId = Guid.NewGuid();

        await ApproveRefundAsync(refundRequestId, orderId, Subtotal);

        Result<int> settled = await Poller.WaitAsync(
            ProjectionTimeout,
            () => RefundStatusAsync(refundRequestId, RefundStatus.Settled));

        settled.IsSuccess.Should().BeTrue();

        // Act — the same approval again, with a fresh envelope so the inbox cannot deduplicate it
        // before the handler ever runs.
        await ApproveRefundAsync(refundRequestId, orderId, Subtotal);

        Result<int> both = await Poller.WaitAsync(
            ProjectionTimeout,
            () => ProcessedApprovalsAsync(refundRequestId, atLeast: 2));

        both.IsSuccess.Should().BeTrue("the redelivery must actually have been processed");

        // Assert — one call, not "one effective refund". A replayed call would be recorded too and
        // would still be a defect: it means the handler asked the provider for the money twice.
        Factory.PaymentGateway
            .CallsFor(FakeGatewayOperation.Refund)
            .Where(c => c.Subject == intentId)
            .Should().ContainSingle("a redelivered approval must not reach the card a second time");

        // And exactly one row, which is what the unique index on the request id is for.
        (await RefundCountAsync(refundRequestId)).Should().Be(1);
    }

    // ---- driving Support and Orders -------------------------------------------------------------

    /// <summary>
    /// Support's approval, published onto the bus. Support is not hosted here — see the class
    /// remarks — so this stands in for an administrator pressing approve.
    /// </summary>
    private async Task ApproveRefundAsync(Guid refundRequestId, Guid orderId, decimal amount)
    {
        await using AsyncServiceScope scope = Factory.Services.CreateAsyncScope();

        var eventBus = scope.ServiceProvider.GetRequiredService<IEventBus>();

        await eventBus.PublishAsync(
            new RefundApprovedIntegrationEvent(
                Guid.NewGuid(),
                DateTime.UtcNow,
                refundRequestId,
                ticketId: Guid.NewGuid(),
                TicketReference,
                orderId,
                Factory.CustomerUserId,
                amount,
                requestedByAgentId: Guid.NewGuid(),
                decidedByAdminId: Guid.NewGuid(),
                decisionNote: "Order arrived cold",
                DateTime.UtcNow),
            TestContext.Current.CancellationToken);
    }

    /// <summary>A card order whose hold has been taken — the only state a refund is legal from.</summary>
    private async Task<Guid> PlaceCapturedCardOrderAsync()
    {
        Guid orderId = await PlaceAuthorizedCardOrderAsync();

        await AcceptAsync(orderId);

        Result<int> captured = await Poller.WaitAsync(
            ProjectionTimeout,
            () => PaymentStatusAsync(orderId, PaymentStatus.Captured));

        captured.IsSuccess.Should().BeTrue("there is nothing to refund until the money has been taken");

        return orderId;
    }

    private async Task<Guid> PlaceAuthorizedCardOrderAsync()
    {
        await GiveTheCustomerACardAsync();

        Guid orderId = await PlaceOrderAsync(OrderPaymentMethods.Card);

        Result<int> authorized = await Poller.WaitAsync(
            ProjectionTimeout,
            () => PaymentStatusAsync(orderId, PaymentStatus.Authorized));

        authorized.IsSuccess.Should().BeTrue();

        // Orders must have projected it too, or Order.Accept() refuses (§8.2).
        Result<int> projected = await Poller.WaitAsync(
            ProjectionTimeout,
            () => OrderPaymentStatusAsync(orderId, expected: 3));

        projected.IsSuccess.Should().BeTrue();

        return orderId;
    }

    private async Task AcceptAsync(Guid orderId)
    {
        string accessToken = await GetAccessTokenAsync(Factory.ManagerUserEmail, Factory.TestUserPassword);

        HttpClient manager = Factory.OrdersApi.CreateClient();
        manager.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        HttpResponseMessage accepted = await manager.PostAsync(
            new Uri($"orders/{orderId}/accept", UriKind.Relative),
            content: null,
            TestContext.Current.CancellationToken);

        accepted.StatusCode.Should().Be(HttpStatusCode.NoContent);
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

    /// <summary>A restaurant owned by the seeded manager, so the accept in the middle of this flow is not a 404.</summary>
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

    // ---- reading both databases ------------------------------------------------------------------

    private async Task<Result<int>> RefundStatusAsync(Guid refundRequestId, RefundStatus expected)
    {
        await using var connection = new NpgsqlConnection(Factory.ConnectionString);

        const string sql =
            "SELECT COUNT(*) FROM refunds WHERE refund_request_id = @RefundRequestId AND status = @Status";

        long matched = await connection.ExecuteScalarAsync<long>(
            sql,
            new { RefundRequestId = refundRequestId, Status = (int)expected });

        return matched > 0
            ? Result.Success((int)expected)
            : Result.Failure<int>(Error.NotFound("Test.RefundNotYet", "The refund has not reached that status"));
    }

    private async Task<string?> RefundFailureReasonAsync(Guid refundRequestId)
    {
        await using var connection = new NpgsqlConnection(Factory.ConnectionString);

        const string sql = "SELECT failure_reason FROM refunds WHERE refund_request_id = @RefundRequestId";

        return await connection.QuerySingleOrDefaultAsync<string?>(sql, new { RefundRequestId = refundRequestId });
    }

    private async Task<long> RefundCountAsync(Guid refundRequestId)
    {
        await using var connection = new NpgsqlConnection(Factory.ConnectionString);

        const string sql = "SELECT COUNT(*) FROM refunds WHERE refund_request_id = @RefundRequestId";

        return await connection.ExecuteScalarAsync<long>(sql, new { RefundRequestId = refundRequestId });
    }

    /// <summary>
    /// How many copies of one approval the Payments inbox has finished with — what makes the
    /// redelivery assertion deterministic rather than a race against Quartz. Note
    /// <c>content::text</c>: the column is jsonb, and <c>LIKE</c> has no jsonb overload.
    /// </summary>
    private async Task<Result<int>> ProcessedApprovalsAsync(Guid refundRequestId, int atLeast)
    {
        await using var connection = new NpgsqlConnection(Factory.ConnectionString);

        const string sql =
            """
            SELECT COUNT(*)
            FROM inbox_messages
            WHERE type LIKE '%RefundApprovedIntegrationEvent%'
              AND content::text LIKE @RefundPattern
              AND processed_on_utc IS NOT NULL
            """;

        long processed = await connection.ExecuteScalarAsync<long>(
            sql,
            new { RefundPattern = $"%{refundRequestId}%" });

        return processed >= atLeast
            ? Result.Success((int)processed)
            : Result.Failure<int>(Error.NotFound("Test.InboxNotYet", "The approval has not been processed yet"));
    }

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

    private async Task<int?> PaymentStatusValueAsync(Guid orderId)
    {
        await using var connection = new NpgsqlConnection(Factory.ConnectionString);

        const string sql = "SELECT status FROM payments WHERE order_id = @OrderId";

        return await connection.QuerySingleOrDefaultAsync<int?>(sql, new { OrderId = orderId });
    }

    private async Task<string> IntentIdAsync(Guid orderId)
    {
        await using var connection = new NpgsqlConnection(Factory.ConnectionString);

        const string sql = "SELECT stripe_payment_intent_id FROM payments WHERE order_id = @OrderId";

        string? intentId = await connection.QuerySingleOrDefaultAsync<string?>(sql, new { OrderId = orderId });

        intentId.Should().NotBeNullOrEmpty("the hold is held against a provider intent");

        return intentId!;
    }

    private async Task<Result<Guid>> ProfileExistsAsync(Guid customerId)
    {
        await using var connection = new NpgsqlConnection(Factory.ConnectionString);

        const string sql = "SELECT COUNT(*) FROM customer_payment_profiles WHERE id = @CustomerId";

        long matched = await connection.ExecuteScalarAsync<long>(sql, new { CustomerId = customerId });

        return matched > 0
            ? Result.Success(customerId)
            : Result.Failure<Guid>(Error.NotFound("Test.ProfileNotYet", "The payment profile does not exist yet"));
    }

    private async Task<Result<bool>> CardFlagAsync(Guid customerId)
    {
        await using var connection = new NpgsqlConnection(Factory.OrdersApi.ConnectionString);

        const string sql = "SELECT can_pay_by_card FROM customer_payment_profiles WHERE id = @CustomerId";

        bool? flag = await connection.QuerySingleOrDefaultAsync<bool?>(sql, new { CustomerId = customerId });

        return flag == true
            ? Result.Success(true)
            : Result.Failure<bool>(Error.NotFound("Test.CardFlagNotYet", "Orders has not seen the card yet"));
    }

    private async Task<Result<Guid>> OrdersCustomerAsync(Guid customerId)
    {
        await using var connection = new NpgsqlConnection(Factory.OrdersApi.ConnectionString);

        // `customers`, not `users` — Orders names its replica after the role it replicates, and the
        // table `users` does not exist in that database at all.
        const string sql = "SELECT COUNT(*) FROM customers WHERE id = @CustomerId";

        long matched = await connection.ExecuteScalarAsync<long>(sql, new { CustomerId = customerId });

        return matched > 0
            ? Result.Success(customerId)
            : Result.Failure<Guid>(Error.NotFound("Test.OrdersUserNotYet", "Orders has not replicated the customer"));
    }

    private async Task<Result<int>> OrderPaymentStatusAsync(Guid orderId, int expected)
    {
        await using var connection = new NpgsqlConnection(Factory.OrdersApi.ConnectionString);

        const string sql = "SELECT COUNT(*) FROM orders WHERE id = @OrderId AND payment_status = @Status";

        long matched = await connection.ExecuteScalarAsync<long>(
            sql,
            new { OrderId = orderId, Status = expected });

        return matched > 0
            ? Result.Success(expected)
            : Result.Failure<int>(Error.NotFound("Test.OrderPaymentNotYet", "Orders has not projected that status"));
    }
}

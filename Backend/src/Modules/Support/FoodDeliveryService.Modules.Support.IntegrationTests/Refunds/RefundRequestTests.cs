using System.Net;
using System.Net.Http.Json;
using AwesomeAssertions;
using FoodDeliveryService.Common.Domain;
using FoodDeliveryService.Modules.Notifications.Domain.Notifications;
using FoodDeliveryService.Modules.Notifications.Infrastructure.Database;
using FoodDeliveryService.Modules.Orders.IntegrationEvents;
using FoodDeliveryService.Modules.Support.Application.Refunds.GetRefundRequests;
using FoodDeliveryService.Modules.Support.Application.Tickets.GetTicketAudit;
using FoodDeliveryService.Modules.Support.Domain.Audit;
using FoodDeliveryService.Modules.Support.Domain.Orders;
using FoodDeliveryService.Modules.Support.Domain.Refunds;
using FoodDeliveryService.Modules.Support.Infrastructure.Database;
using FoodDeliveryService.Modules.Support.IntegrationTests.Abstractions;
using FoodDeliveryService.Modules.Support.Presentation.Refunds;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FoodDeliveryService.Modules.Support.IntegrationTests.Refunds;

/// <summary>
/// The refund workflow end to end. Three of these claims cannot be made anywhere but here: that the
/// subtotal ceiling is checked against a replica built from a real broker delivery, that two
/// concurrent approvals produce exactly one, and that a decision reaches the customer's mailbox in
/// another service's database.
/// <para>
/// Every test seeds its own order by publishing <c>OrderPlacedIntegrationEvent</c> onto the shared
/// broker and waiting for Support's projection to catch up — the same path production takes, and the
/// only one that proves the ceiling is not being read from somewhere it should not be.
/// </para>
/// </summary>
public class RefundRequestTests(IntegrationTestWebAppFactory factory) : BaseIntegrationTest(factory)
{
    private const decimal OrderSubtotal = 42.50m;

    [Fact]
    public async Task RequestRefund_ThenApprove_ShouldNotifyTheCustomer_AndRecordBothAuditEntries()
    {
        // Arrange — a unique subject makes this test's notification row identifiable among every
        // other row in the Notifications database the whole suite shares.
        HttpClient customerClient = await CreateCustomerClientAsync();
        HttpClient agentClient = await CreateAgentClientAsync();
        HttpClient adminClient = await CreateAdminClientAsync();

        Guid orderId = await SeedOrderAsync();
        Guid ticketId = await OpenTicketAsync(customerClient, $"Refund approval {orderId:N}", orderId: orderId);

        // Act
        Guid refundRequestId = await RequestRefundAsync(agentClient, ticketId, 12.34m);

        HttpResponseMessage approval = await ApproveAsync(adminClient, refundRequestId);

        // Assert
        approval.StatusCode.Should().Be(HttpStatusCode.NoContent);

        RefundRequest stored = await GetRefundRequestAsync(refundRequestId);
        stored.Status.Should().Be(RefundStatus.Approved);
        stored.Amount.Should().Be(12.34m);
        stored.RequestedByAgentId.Should().Be(Factory.AgentUserId);

        // The two halves of segregation of duties, as the record shows them.
        stored.DecidedByAdminId.Should().NotBeNull().And.NotBe(stored.RequestedByAgentId);

        // Both actions on the ticket's history, keyed on the ticket, so somebody reviewing the case
        // sees the refund without having to know to look in a second place.
        IReadOnlyCollection<SupportAuditEntryResponse> audit = await GetAuditAsync(agentClient, ticketId);

        audit.Should().ContainSingle(e => e.Action == SupportAuditAction.RefundRequested);
        audit.Should().ContainSingle(e => e.Action == SupportAuditAction.RefundApproved);
        audit.Single(e => e.Action == SupportAuditAction.RefundRequested).ActorId.Should().Be(Factory.AgentUserId);

        // Support's outbox publishes, the broker delivers, Notifications consumes and logs the send
        // in its own database. Sixty seconds because the path crosses two poll intervals.
        Result<Notification> notification = await WaitForNotificationAsync(
            n => n.RecipientUserId == Factory.CustomerUserId &&
                 n.Type == NotificationType.RefundDecision &&
                 n.Subject.Contains("approved"),
            TimeSpan.FromSeconds(60));

        notification.IsSuccess.Should().BeTrue("an approved refund should email the customer");
        notification.Value.Status.Should().Be(NotificationStatus.Sent);
    }

    [Fact]
    public async Task RejectRefund_ShouldEmailTheCustomerTheDecision()
    {
        // Arrange
        HttpClient customerClient = await CreateCustomerClientAsync();
        HttpClient agentClient = await CreateAgentClientAsync();
        HttpClient adminClient = await CreateAdminClientAsync();

        Guid orderId = await SeedOrderAsync();
        Guid ticketId = await OpenTicketAsync(customerClient, $"Refund rejection {orderId:N}", orderId: orderId);

        Guid refundRequestId = await RequestRefundAsync(agentClient, ticketId, 5m);

        // Act
        HttpResponseMessage rejection = await adminClient.PostAsJsonAsync(
            $"support/refund-requests/{refundRequestId}/reject",
            new RejectRefund.Request { Note = "The order was delivered and signed for" },
            TestContext.Current.CancellationToken);

        // Assert
        rejection.StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await GetRefundRequestAsync(refundRequestId)).Status.Should().Be(RefundStatus.Rejected);

        // A refusal that arrives in silence is indistinguishable, from the customer's side, from a
        // request nobody read — so the declined path is emailed exactly like the approved one.
        Result<Notification> notification = await WaitForNotificationAsync(
            n => n.RecipientUserId == Factory.CustomerUserId &&
                 n.Type == NotificationType.RefundDecision &&
                 n.Subject.Contains("declined"),
            TimeSpan.FromSeconds(60));

        notification.IsSuccess.Should().BeTrue("a declined refund should email the customer too");
    }

    [Fact]
    public async Task ApproveRefund_ShouldReturnForbidden_WhenTheCallerIsAnAgent()
    {
        // Arrange
        HttpClient customerClient = await CreateCustomerClientAsync();
        HttpClient agentClient = await CreateAgentClientAsync();

        Guid orderId = await SeedOrderAsync();
        Guid ticketId = await OpenTicketAsync(customerClient, orderId: orderId);

        Guid refundRequestId = await RequestRefundAsync(agentClient, ticketId, 10m);

        // Act — the agent who raised it tries to approve it.
        HttpResponseMessage response = await ApproveAsync(agentClient, refundRequestId);

        // Assert — 403 at the route policy: refunds:approve is administrator-only, and an agent
        // never reaches the handler at all. The aggregate's own requester check is the second layer
        // behind this, for the administrator who also holds refunds:request.
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await GetRefundRequestAsync(refundRequestId)).Status.Should().Be(RefundStatus.Requested);
    }

    [Fact]
    public async Task ApproveRefund_ShouldReturnBadRequest_WhenTheAdministratorDecidesTheirOwnRequest()
    {
        // Arrange — the administrator holds refunds:request as well as refunds:approve, so they can
        // reach both endpoints. This is the case no permission code can catch.
        HttpClient customerClient = await CreateCustomerClientAsync();
        HttpClient adminClient = await CreateAdminClientAsync();

        Guid orderId = await SeedOrderAsync();
        Guid ticketId = await OpenTicketAsync(customerClient, orderId: orderId);

        Guid refundRequestId = await RequestRefundAsync(adminClient, ticketId, 10m);

        // Act
        HttpResponseMessage response = await ApproveAsync(adminClient, refundRequestId);

        // Assert — refused by the aggregate, which is where segregation of duties actually lives.
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await GetRefundRequestAsync(refundRequestId)).Status.Should().Be(RefundStatus.Requested);
    }

    [Fact]
    public async Task ApproveRefund_ShouldApproveExactlyOnce_WhenTwoDecisionsArriveConcurrently()
    {
        // Arrange
        HttpClient customerClient = await CreateCustomerClientAsync();
        HttpClient agentClient = await CreateAgentClientAsync();

        Guid orderId = await SeedOrderAsync();
        Guid ticketId = await OpenTicketAsync(customerClient, orderId: orderId);

        Guid refundRequestId = await RequestRefundAsync(agentClient, ticketId, 10m);

        using HttpClient firstAdmin = await CreateAdminClientAsync();
        using HttpClient secondAdmin = await CreateAdminClientAsync();

        // Act — CA2025 rejects handing an HttpClient in a using scope to a task that is not awaited
        // in the same statement, so the two calls are awaited directly rather than stored first.
        HttpResponseMessage[] responses = await Task.WhenAll(
            ApproveAsync(firstAdmin, refundRequestId),
            ApproveAsync(secondAdmin, refundRequestId));

        // Assert — one winner. The loser hits the distributed lock if it arrives while the winner
        // holds it, and the aggregate's AlreadyDecided guard if it arrives after the winner
        // committed; both are correct, so asserting on one of them would be a test of timing.
        responses.Count(r => r.StatusCode == HttpStatusCode.NoContent).Should().Be(1);
        responses.Count(r => !r.IsSuccessStatusCode).Should().Be(1);

        RefundRequest stored = await GetRefundRequestAsync(refundRequestId);
        stored.Status.Should().Be(RefundStatus.Approved);

        // The real proof, and the reason this is not just an assertion about status codes: exactly
        // one approval was recorded. A second write would have left two.
        IReadOnlyCollection<SupportAuditEntryResponse> audit = await GetAuditAsync(agentClient, ticketId);
        audit.Count(e => e.Action == SupportAuditAction.RefundApproved).Should().Be(1);

        foreach (HttpResponseMessage response in responses)
        {
            response.Dispose();
        }
    }

    [Fact]
    public async Task RequestRefund_ShouldReturnBadRequest_WhenAmountExceedsTheReplicatedSubtotal()
    {
        // Arrange
        HttpClient customerClient = await CreateCustomerClientAsync();
        HttpClient agentClient = await CreateAgentClientAsync();

        Guid orderId = await SeedOrderAsync();
        Guid ticketId = await OpenTicketAsync(customerClient, orderId: orderId);

        // Act — one cent over the subtotal the OrderPlaced event carried.
        HttpResponseMessage response = await agentClient.PostAsJsonAsync(
            $"support/tickets/{ticketId}/refund-requests",
            new RequestRefund.Request { Amount = OrderSubtotal + 0.01m, Reason = "Wrong order" },
            TestContext.Current.CancellationToken);

        // Assert — the ceiling comes from Support's own replica of the order, never from a call to
        // Orders, which is the point of projecting the snapshot at all.
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task RequestRefund_ShouldReturnBadRequest_WhenTheTicketNamesNoOrder()
    {
        // Arrange
        HttpClient customerClient = await CreateCustomerClientAsync();
        HttpClient agentClient = await CreateAgentClientAsync();

        Guid ticketId = await OpenTicketAsync(customerClient);

        // Act
        HttpResponseMessage response = await agentClient.PostAsJsonAsync(
            $"support/tickets/{ticketId}/refund-requests",
            new RequestRefund.Request { Amount = 5m, Reason = "The app crashes at checkout" },
            TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task RequestRefund_ShouldReturnConflict_WhenTheOrderAlreadyHasALiveRequest()
    {
        // Arrange — two tickets about the same order, which is how two agents end up requesting
        // twice for the same money.
        HttpClient customerClient = await CreateCustomerClientAsync();
        HttpClient agentClient = await CreateAgentClientAsync();
        HttpClient otherAgentClient = await CreateOtherAgentClientAsync();

        Guid orderId = await SeedOrderAsync();
        Guid firstTicketId = await OpenTicketAsync(customerClient, orderId: orderId);
        Guid secondTicketId = await OpenTicketAsync(customerClient, orderId: orderId);

        await RequestRefundAsync(agentClient, firstTicketId, 10m);

        // Act
        HttpResponseMessage response = await otherAgentClient.PostAsJsonAsync(
            $"support/tickets/{secondTicketId}/refund-requests",
            new RequestRefund.Request { Amount = 10m, Reason = "Also asking" },
            TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task GetRefundRequests_ShouldReturnTheQueue_FilteredByStatus()
    {
        // Arrange
        HttpClient customerClient = await CreateCustomerClientAsync();
        HttpClient agentClient = await CreateAgentClientAsync();

        Guid orderId = await SeedOrderAsync();
        Guid ticketId = await OpenTicketAsync(customerClient, orderId: orderId);

        Guid refundRequestId = await RequestRefundAsync(agentClient, ticketId, 7.25m);

        // Act
        HttpResponseMessage response = await agentClient.GetAsync(
            new Uri($"support/refund-requests?status={nameof(RefundStatus.Requested)}", UriKind.Relative),
            TestContext.Current.CancellationToken);

        // Assert
        response.EnsureSuccessStatusCode();

        List<RefundRequestResponse> queue = (await response.Content.ReadFromJsonAsync<List<RefundRequestResponse>>(
            TestContext.Current.CancellationToken))!;

        RefundRequestResponse row = queue.Single(r => r.Id == refundRequestId);
        row.Status.Should().Be(RefundStatus.Requested);
        row.Amount.Should().Be(7.25m);
        row.TicketReference.Should().StartWith("SUP-");

        // Joined from the local agent replica — the reason a queue can render "who asked" with no
        // cross-service call.
        row.RequestedByAgentName.Should().NotBeNullOrWhiteSpace();
        row.DecidedByAdminId.Should().BeNull();
    }

    [Fact]
    public async Task GetRefundRequests_ShouldReturnForbidden_ForACustomer()
    {
        // Arrange
        HttpClient customerClient = await CreateCustomerClientAsync();

        // Act
        HttpResponseMessage response = await customerClient.GetAsync(
            new Uri("support/refund-requests", UriKind.Relative),
            TestContext.Current.CancellationToken);

        // Assert — a customer holds neither refunds code, and the queue carries other customers'
        // amounts and agents' reasons. They learn about their own refund from the decision email.
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    /// <summary>
    /// Publishes an order onto the broker and waits for Support's projection to build the snapshot.
    /// This is the whole reason the refund ceiling is checkable at all: hard rule #5 forbids asking
    /// Orders, so the subtotal has to arrive as an event or not at all.
    /// </summary>
    private async Task<Guid> SeedOrderAsync()
    {
        var orderId = Guid.NewGuid();

        await Factory.PublishAsync(
            new OrderPlacedIntegrationEvent(
                Guid.NewGuid(),
                DateTime.UtcNow,
                orderId,
                Factory.CustomerUserId,
                restaurantId: Guid.NewGuid(),
                OrderSubtotal,
                placedOnUtc: DateTime.UtcNow),
            TestContext.Current.CancellationToken);

        Result<OrderSnapshot> snapshot = await Poller.WaitAsync<OrderSnapshot>(
            TimeSpan.FromSeconds(60),
            async () =>
            {
                await using AsyncServiceScope scope = Factory.Services.CreateAsyncScope();

                var context = scope.ServiceProvider.GetRequiredService<SupportDbContext>();

                return await context.Set<OrderSnapshot>()
                    .AsNoTracking()
                    .FirstOrDefaultAsync(o => o.Id == orderId, TestContext.Current.CancellationToken);
            });

        snapshot.IsSuccess.Should().BeTrue("the order replica must be built before a refund can be capped");
        snapshot.Value.Subtotal.Should().Be(OrderSubtotal);

        return orderId;
    }

    private static async Task<Guid> RequestRefundAsync(HttpClient client, Guid ticketId, decimal amount)
    {
        HttpResponseMessage response = await client.PostAsJsonAsync(
            $"support/tickets/{ticketId}/refund-requests",
            new RequestRefund.Request { Amount = amount, Reason = "Order never arrived" },
            TestContext.Current.CancellationToken);

        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<Guid>(TestContext.Current.CancellationToken);
    }

    private static async Task<HttpResponseMessage> ApproveAsync(HttpClient client, Guid refundRequestId) =>
        await client.PostAsJsonAsync(
            $"support/refund-requests/{refundRequestId}/approve",
            new ApproveRefund.Request { Note = "Confirmed with the restaurant" },
            TestContext.Current.CancellationToken);

    private static async Task<IReadOnlyCollection<SupportAuditEntryResponse>> GetAuditAsync(
        HttpClient client,
        Guid ticketId)
    {
        HttpResponseMessage response = await client.GetAsync(
            new Uri($"support/tickets/{ticketId}/audit", UriKind.Relative),
            TestContext.Current.CancellationToken);

        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadFromJsonAsync<List<SupportAuditEntryResponse>>(
            TestContext.Current.CancellationToken))!;
    }

    /// <summary>Reads the row back from Support's own database — the record, not the response.</summary>
    private async Task<RefundRequest> GetRefundRequestAsync(Guid refundRequestId)
    {
        await using AsyncServiceScope scope = Factory.Services.CreateAsyncScope();

        var context = scope.ServiceProvider.GetRequiredService<SupportDbContext>();

        return await context.Set<RefundRequest>()
            .AsNoTracking()
            .SingleAsync(r => r.Id == refundRequestId, TestContext.Current.CancellationToken);
    }

    private async Task<Result<Notification>> WaitForNotificationAsync(
        Func<Notification, bool> predicate,
        TimeSpan timeout) =>
        await Poller.WaitAsync<Notification>(
            timeout,
            async () =>
            {
                await using AsyncServiceScope scope = Factory.NotificationsApi.Services.CreateAsyncScope();

                var context = scope.ServiceProvider.GetRequiredService<NotificationsDbContext>();

                // Notification? → Result<Notification>: null converts to Failure(Error.NullValue),
                // which keeps the poller retrying until the send is logged.
                List<Notification> candidates = await context.Set<Notification>()
                    .AsNoTracking()
                    .Where(n => n.Type == NotificationType.RefundDecision)
                    .ToListAsync(TestContext.Current.CancellationToken);

                return candidates.FirstOrDefault(predicate);
            });
}

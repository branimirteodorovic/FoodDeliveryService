using AwesomeAssertions;
using FoodDeliveryService.Common.Domain;
using FoodDeliveryService.Modules.Notifications.Domain.Notifications;
using FoodDeliveryService.Modules.Notifications.Infrastructure.Database;
using FoodDeliveryService.Modules.Orders.IntegrationEvents;
using FoodDeliveryService.Modules.Payments.IntegrationEvents;
using FoodDeliveryService.Modules.Support.Application.Tickets.GetTicketAudit;
using FoodDeliveryService.Modules.Support.Domain.Audit;
using FoodDeliveryService.Modules.Support.Domain.Orders;
using FoodDeliveryService.Modules.Support.Domain.Refunds;
using FoodDeliveryService.Modules.Support.Infrastructure.Database;
using FoodDeliveryService.Modules.Support.IntegrationTests.Abstractions;
using FoodDeliveryService.Modules.Support.Presentation.Refunds;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System.Net.Http.Json;

namespace FoodDeliveryService.Modules.Support.IntegrationTests.Refunds;

/// <summary>
/// What Payments does with an approved refund, coming back — Feature 3.8 Milestone H, §10.2.
/// <para>
/// The other half of <c>RefundRequestTests</c>. That suite ends where an administrator presses
/// approve, which is where this whole workflow used to end full stop: `SUPPORT_PHASE3_PLAN.md`
/// decided that no money would move, and eleven places in the codebase said so. §10.3 retires that,
/// and these are the tests that hold the retraction up — an approval now has an answer, and the two
/// claims worth proving are that a settlement is recorded with an audit entry nobody wrote by hand,
/// and that a <em>failure</em> is too.
/// </para>
/// <para>
/// Payments is not hosted here, so its two events are published onto the shared broker. Everything
/// after the publish is real: the consumer registration, the inbox, the handler, the lock, the
/// aggregate and the audit entry that commits with it.
/// </para>
/// </summary>
public class RefundSettlementTests(IntegrationTestWebAppFactory factory) : BaseIntegrationTest(factory)
{
    private static readonly TimeSpan SettlementTimeout = TimeSpan.FromSeconds(60);

    private const decimal OrderSubtotal = 42.50m;

    [Fact]
    public async Task ASettledRefund_ShouldCloseTheRequest_AndAuditItAsTheSystem()
    {
        // Arrange — a real approval, through the real endpoints, by a different administrator.
        Approval approval = await ApprovedRefundAsync(12.34m);

        // Act — Payments says the money went back.
        await Factory.PublishAsync(
            new RefundSettledIntegrationEvent(
                Guid.NewGuid(),
                DateTime.UtcNow,
                refundId: Guid.NewGuid(),
                approval.RefundRequestId,
                approval.TicketId,
                approval.TicketReference,
                approval.OrderId,
                Factory.CustomerUserId,
                amount: 12.34m,
                currency: "EUR",
                settledOnUtc: DateTime.UtcNow),
            TestContext.Current.CancellationToken);

        // Assert
        Result<RefundRequest> settled = await WaitForStatusAsync(
            approval.RefundRequestId,
            RefundStatus.Settled);

        settled.IsSuccess.Should().BeTrue("an approval that Payments paid must stop saying 'approved'");
        settled.Value.SettledOnUtc.Should().NotBeNull();
        settled.Value.FailureReason.Should().BeNull();

        // The audit entry, in the ticket's own history where a reviewer of the case will see it —
        // and attributed to the platform, because no agent performed it. This is the first entry in
        // the log that is not a person's action, and the actor is what says so.
        HttpClient agentClient = await CreateAgentClientAsync();

        IReadOnlyCollection<SupportAuditEntryResponse> audit = await GetAuditAsync(agentClient, approval.TicketId);

        SupportAuditEntryResponse entry = audit.Single(e => e.Action == SupportAuditAction.RefundSettled);
        entry.ActorId.Should().Be(SupportAuditEntry.SystemActorId);

        // Side by side with the approval it answers, which is the pair that makes "an agent refunded
        // this order" checkable end to end rather than merely agreed to.
        audit.Should().ContainSingle(e => e.Action == SupportAuditAction.RefundApproved);
    }

    [Fact]
    public async Task AFailedRefund_ShouldRecordTheReason_RatherThanLeaveTheRequestApproved()
    {
        // Arrange
        Approval approval = await ApprovedRefundAsync(9.99m);

        // Act — the order was paid in cash, so Payments has nothing to give back. The approval is
        // not wrong; it just cannot be settled by this platform.
        await Factory.PublishAsync(
            new RefundFailedIntegrationEvent(
                Guid.NewGuid(),
                DateTime.UtcNow,
                refundId: Guid.NewGuid(),
                approval.RefundRequestId,
                approval.TicketId,
                approval.TicketReference,
                approval.OrderId,
                Factory.CustomerUserId,
                amount: 9.99m,
                currency: "EUR",
                reason: "not_card_payment",
                failedOnUtc: DateTime.UtcNow),
            TestContext.Current.CancellationToken);

        // Assert — this is the test that keeps the reversal honest. Making refunds real is easy to
        // do halfway: publish the approval, let Payments try, and leave the request saying
        // "approved" when it does not work. Then an agent has told a customer their money is coming
        // back and nothing anywhere disagrees.
        Result<RefundRequest> failed = await WaitForStatusAsync(approval.RefundRequestId, RefundStatus.Failed);

        failed.IsSuccess.Should().BeTrue();
        failed.Value.FailureReason.Should().Be("not_card_payment");
        failed.Value.FailedOnUtc.Should().NotBeNull();

        // The approval trail is untouched — what changed is that the request stops claiming to be in
        // progress, not who agreed to it.
        failed.Value.DecidedByAdminId.Should().NotBeNull();

        HttpClient agentClient = await CreateAgentClientAsync();

        IReadOnlyCollection<SupportAuditEntryResponse> audit = await GetAuditAsync(agentClient, approval.TicketId);

        SupportAuditEntryResponse entry = audit.Single(e => e.Action == SupportAuditAction.RefundFailed);
        entry.ActorId.Should().Be(SupportAuditEntry.SystemActorId);
        entry.ToValue.Should().Be("not_card_payment", "a failed refund an agent cannot explain is not actionable");
    }

    [Fact]
    public async Task ASettledRefund_ShouldEmailTheCustomerThatTheMoneyIsOnItsWay()
    {
        // Arrange
        Approval approval = await ApprovedRefundAsync(15.00m);

        // Act
        await Factory.PublishAsync(
            new RefundSettledIntegrationEvent(
                Guid.NewGuid(),
                DateTime.UtcNow,
                refundId: Guid.NewGuid(),
                approval.RefundRequestId,
                approval.TicketId,
                approval.TicketReference,
                approval.OrderId,
                Factory.CustomerUserId,
                amount: 15.00m,
                currency: "EUR",
                settledOnUtc: DateTime.UtcNow),
            TestContext.Current.CancellationToken);

        // Assert — a second email after the decision one, deliberately: they answer different
        // questions, and until this feature the platform could only ever answer the first.
        Result<Notification> notification = await Poller.WaitAsync<Notification>(
            SettlementTimeout,
            async () =>
            {
                await using AsyncServiceScope scope = Factory.NotificationsApi.Services.CreateAsyncScope();

                var context = scope.ServiceProvider.GetRequiredService<NotificationsDbContext>();

                List<Notification> candidates = await context.Set<Notification>()
                    .AsNoTracking()
                    .Where(n => n.Type == NotificationType.RefundSettled)
                    .ToListAsync(TestContext.Current.CancellationToken);

                return candidates.FirstOrDefault(n =>
                    n.RecipientUserId == Factory.CustomerUserId &&
                    n.Subject.Contains(approval.TicketReference, StringComparison.Ordinal));
            });

        notification.IsSuccess.Should().BeTrue(
            "RefundSettled needs an enum member, a template arm AND a NotificationChannelRouter route — " +
            "a type missing from that map sends nothing and reports success");
    }

    [Fact]
    public async Task ARedeliveredSettlement_ShouldNotWriteASecondAuditEntry()
    {
        // Arrange — the inbox dispatches at least once, and a duplicate entry claiming the money
        // moved twice is the misleading half of that: the aggregate's own transition is idempotent,
        // but it cannot see the audit row the handler writes beside it.
        Approval approval = await ApprovedRefundAsync(5.50m);

        RefundSettledIntegrationEvent Settlement() => new(
            Guid.NewGuid(),
            DateTime.UtcNow,
            refundId: Guid.NewGuid(),
            approval.RefundRequestId,
            approval.TicketId,
            approval.TicketReference,
            approval.OrderId,
            Factory.CustomerUserId,
            amount: 5.50m,
            currency: "EUR",
            settledOnUtc: DateTime.UtcNow);

        await Factory.PublishAsync(Settlement(), TestContext.Current.CancellationToken);

        Result<RefundRequest> settled = await WaitForStatusAsync(approval.RefundRequestId, RefundStatus.Settled);
        settled.IsSuccess.Should().BeTrue();

        DateTime? firstSettledOn = settled.Value.SettledOnUtc;

        // Act — a fresh envelope, so the inbox cannot deduplicate it before the handler runs.
        await Factory.PublishAsync(Settlement(), TestContext.Current.CancellationToken);

        Result<int> processed = await Poller.WaitAsync(
            SettlementTimeout,
            () => ProcessedSettlementsAsync(approval.RefundRequestId, atLeast: 2));

        processed.IsSuccess.Should().BeTrue("the redelivery must actually have been processed");

        // Assert
        HttpClient agentClient = await CreateAgentClientAsync();

        IReadOnlyCollection<SupportAuditEntryResponse> audit = await GetAuditAsync(agentClient, approval.TicketId);

        audit.Count(e => e.Action == SupportAuditAction.RefundSettled).Should().Be(1);

        // And the first settlement's timestamp stands, so the row and the trail agree on when.
        RefundRequest stored = await GetRefundRequestAsync(approval.RefundRequestId);
        stored.SettledOnUtc.Should().Be(firstSettledOn);
    }

    // ---- getting to an approved refund ----------------------------------------------------------

    private sealed record Approval(Guid RefundRequestId, Guid TicketId, string TicketReference, Guid OrderId);

    private async Task<Approval> ApprovedRefundAsync(decimal amount)
    {
        HttpClient customerClient = await CreateCustomerClientAsync();
        HttpClient agentClient = await CreateAgentClientAsync();
        HttpClient adminClient = await CreateAdminClientAsync();

        Guid orderId = await SeedOrderAsync();
        Guid ticketId = await OpenTicketAsync(customerClient, $"Refund settlement {orderId:N}", orderId: orderId);

        HttpResponseMessage requested = await agentClient.PostAsJsonAsync(
            $"support/tickets/{ticketId}/refund-requests",
            new RequestRefund.Request { Amount = amount, Reason = "Order arrived cold" },
            TestContext.Current.CancellationToken);

        requested.EnsureSuccessStatusCode();

        Guid refundRequestId = await requested.Content.ReadFromJsonAsync<Guid>(TestContext.Current.CancellationToken);

        HttpResponseMessage approved = await adminClient.PostAsJsonAsync(
            $"support/refund-requests/{refundRequestId}/approve",
            new ApproveRefund.Request { Note = "Confirmed with the restaurant" },
            TestContext.Current.CancellationToken);

        approved.EnsureSuccessStatusCode();

        RefundRequest stored = await GetRefundRequestAsync(refundRequestId);

        return new Approval(refundRequestId, ticketId, stored.TicketReference, orderId);
    }

    /// <summary>The order replica the refund ceiling is read from — see <c>RefundRequestTests</c>.</summary>
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
                paymentMethod: OrderPaymentMethods.Card,
                placedOnUtc: DateTime.UtcNow),
            TestContext.Current.CancellationToken);

        Result<OrderSnapshot> snapshot = await Poller.WaitAsync<OrderSnapshot>(
            SettlementTimeout,
            async () =>
            {
                await using AsyncServiceScope scope = Factory.Services.CreateAsyncScope();

                var context = scope.ServiceProvider.GetRequiredService<SupportDbContext>();

                return await context.Set<OrderSnapshot>()
                    .AsNoTracking()
                    .FirstOrDefaultAsync(o => o.Id == orderId, TestContext.Current.CancellationToken);
            });

        snapshot.IsSuccess.Should().BeTrue("the order replica must be built before a refund can be capped");

        return orderId;
    }

    // ---- reading the record ----------------------------------------------------------------------

    private async Task<Result<RefundRequest>> WaitForStatusAsync(Guid refundRequestId, RefundStatus expected) =>
        await Poller.WaitAsync<RefundRequest>(
            SettlementTimeout,
            async () =>
            {
                await using AsyncServiceScope scope = Factory.Services.CreateAsyncScope();

                var context = scope.ServiceProvider.GetRequiredService<SupportDbContext>();

                return await context.Set<RefundRequest>()
                    .AsNoTracking()
                    .FirstOrDefaultAsync(
                        r => r.Id == refundRequestId && r.Status == expected,
                        TestContext.Current.CancellationToken);
            });

    /// <summary>
    /// How many copies of one settlement Support's inbox has finished with — what makes the
    /// redelivery assertion deterministic rather than a race against Quartz.
    /// <para>
    /// Two things in this statement are not optional. <c>content::text</c>, because the column is
    /// jsonb and <c>LIKE</c> has no jsonb overload; and the <c>"Value"</c> alias, because
    /// <c>SqlQueryRaw&lt;int&gt;</c> projects a scalar result by looking for a column of exactly
    /// that name and fails at the database with <c>column s.Value does not exist</c> without it.
    /// </para>
    /// </summary>
    private async Task<Result<int>> ProcessedSettlementsAsync(Guid refundRequestId, int atLeast)
    {
        await using AsyncServiceScope scope = Factory.Services.CreateAsyncScope();

        var context = scope.ServiceProvider.GetRequiredService<SupportDbContext>();

        const string sql =
            """
            SELECT COUNT(*)::int AS "Value"
            FROM inbox_messages
            WHERE type LIKE '%RefundSettledIntegrationEvent%'
              AND content::text LIKE @pattern
              AND processed_on_utc IS NOT NULL
            """;

        int processed = await context.Database
            .SqlQueryRaw<int>(sql, new Npgsql.NpgsqlParameter("pattern", $"%{refundRequestId}%"))
            .SingleAsync(TestContext.Current.CancellationToken);

        return processed >= atLeast
            ? Result.Success(processed)
            : Result.Failure<int>(Error.NotFound("Test.InboxNotYet", "The settlement has not been processed yet"));
    }

    private async Task<RefundRequest> GetRefundRequestAsync(Guid refundRequestId)
    {
        await using AsyncServiceScope scope = Factory.Services.CreateAsyncScope();

        var context = scope.ServiceProvider.GetRequiredService<SupportDbContext>();

        return await context.Set<RefundRequest>()
            .AsNoTracking()
            .SingleAsync(r => r.Id == refundRequestId, TestContext.Current.CancellationToken);
    }

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
}

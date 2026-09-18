using AwesomeAssertions;
using FoodDeliveryService.Common.Domain;
using FoodDeliveryService.Modules.Support.Domain.Refunds;
using FoodDeliveryService.Modules.Support.UnitTests.Abstractions;

namespace FoodDeliveryService.Modules.Support.UnitTests.Refunds;

/// <summary>
/// The refund aggregate's four rules, all of which are the aggregate's own and none of which a
/// permission check could enforce: the amount ceiling, the at-most-one-live-request rule, decided
/// once, and never decided by the requester.
/// <para>
/// The last of those is the reason this feature has two steps at all. <c>refunds:approve</c> being
/// admin-only keeps agents off the endpoint, but it says nothing about an administrator who also
/// holds <c>refunds:request</c> — so the check that actually delivers segregation of duties is the
/// one asserted here, on the aggregate.
/// </para>
/// </summary>
public class RefundRequestTests : BaseTest
{
    private const string TicketReference = "SUP-00000001";

    private const decimal OrderSubtotal = 42.50m;

    private static readonly DateTime UtcNow = new(2026, 8, 31, 12, 0, 0, DateTimeKind.Utc);

    private static readonly Guid TicketId = Guid.NewGuid();

    private static readonly Guid OrderId = Guid.NewGuid();

    private static Result<RefundRequest> CreateRequest(
        decimal amount = 10m,
        decimal orderSubtotal = OrderSubtotal,
        bool orderHasActiveRefundRequest = false,
        string reason = "Order never arrived",
        Guid? requestedByAgentId = null,
        Guid? ticketOrderId = null)
    {
        return RefundRequest.Create(
            TicketId,
            TicketReference,
            ticketOrderId ?? OrderId,
            customerId: Guid.NewGuid(),
            amount,
            orderSubtotal,
            orderHasActiveRefundRequest,
            reason,
            requestedByAgentId ?? Guid.NewGuid(),
            UtcNow);
    }

    /// <summary>A request sitting in the approval queue, with its creation event cleared.</summary>
    private static RefundRequest RequestedBy(Guid agentId)
    {
        RefundRequest request = CreateRequest(requestedByAgentId: agentId).Value;
        request.ClearDomainEvents();

        return request;
    }

    /// <summary>An approved request, waiting on Payments to say what became of the money.</summary>
    private static RefundRequest Approved()
    {
        RefundRequest request = RequestedBy(Guid.NewGuid());
        request.Approve(Guid.NewGuid(), "Agreed", UtcNow);
        request.ClearDomainEvents();

        return request;
    }

    [Fact]
    public void Create_ShouldSucceed_AndRaiseRequestedEvent()
    {
        // Arrange
        var agentId = Guid.NewGuid();

        // Act
        Result<RefundRequest> result = CreateRequest(amount: 12.34m, requestedByAgentId: agentId);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Status.Should().Be(RefundStatus.Requested);
        result.Value.Amount.Should().Be(12.34m);
        result.Value.RequestedByAgentId.Should().Be(agentId);
        result.Value.TicketReference.Should().Be(TicketReference);
        result.Value.DecidedByAdminId.Should().BeNull();
        result.Value.DecidedOnUtc.Should().BeNull();

        RefundRequestedDomainEvent raised =
            AssertDomainEventWasPublished<RefundRequestedDomainEvent>(result.Value);

        raised.Amount.Should().Be(12.34m);
        raised.TicketReference.Should().Be(TicketReference);
    }

    [Fact]
    public void Create_ShouldSucceed_WhenAmountEqualsTheOrderSubtotal()
    {
        // Act — the boundary the ceiling rule turns on. A full refund is the ordinary outcome of
        // "the food never arrived", so an off-by-one here would refuse the most common case.
        Result<RefundRequest> result = CreateRequest(amount: OrderSubtotal);

        // Assert
        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void Create_ShouldFail_WhenAmountExceedsTheOrderSubtotal()
    {
        // Act
        Result<RefundRequest> result = CreateRequest(amount: OrderSubtotal + 0.01m);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(RefundErrors.AmountExceedsOrderSubtotal);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-0.01)]
    public void Create_ShouldFail_WhenAmountIsNotPositive(decimal amount)
    {
        // Act
        Result<RefundRequest> result = CreateRequest(amount);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(RefundErrors.AmountNotPositive);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_ShouldFail_WhenReasonIsMissing(string reason)
    {
        // Act
        Result<RefundRequest> result = CreateRequest(reason: reason);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(RefundErrors.ReasonRequired);
    }

    [Fact]
    public void Create_ShouldFail_WhenTheTicketNamesNoOrder()
    {
        // Act — a ticket about the app itself has nothing to refund.
        Result<RefundRequest> result = RefundRequest.Create(
            TicketId,
            TicketReference,
            ticketOrderId: null,
            customerId: Guid.NewGuid(),
            amount: 10m,
            OrderSubtotal,
            orderHasActiveRefundRequest: false,
            "Order never arrived",
            Guid.NewGuid(),
            UtcNow);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(RefundErrors.TicketHasNoOrder);
    }

    [Fact]
    public void Create_ShouldFail_WhenTheOrderAlreadyHasALiveRequest()
    {
        // Act — two agents on two tickets for the same order is the case this exists for.
        Result<RefundRequest> result = CreateRequest(orderHasActiveRefundRequest: true);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(RefundErrors.AlreadyRequestedForOrder);
    }

    [Fact]
    public void Approve_ShouldSucceed_AndRaiseApprovedEvent()
    {
        // Arrange
        var agentId = Guid.NewGuid();
        var adminId = Guid.NewGuid();
        RefundRequest request = RequestedBy(agentId);

        // Act
        Result result = request.Approve(adminId, "Confirmed with the restaurant", UtcNow);

        // Assert
        result.IsSuccess.Should().BeTrue();
        request.Status.Should().Be(RefundStatus.Approved);
        request.DecidedByAdminId.Should().Be(adminId);
        request.DecidedOnUtc.Should().Be(UtcNow);

        RefundApprovedDomainEvent raised = AssertDomainEventWasPublished<RefundApprovedDomainEvent>(request);

        // Both actors travel on the event, which is what lets segregation of duties be verified
        // from outside Support without asking it anything.
        raised.RequestedByAgentId.Should().Be(agentId);
        raised.DecidedByAdminId.Should().Be(adminId);
    }

    [Fact]
    public void Reject_ShouldSucceed_AndRaiseRejectedEvent()
    {
        // Arrange
        var agentId = Guid.NewGuid();
        var adminId = Guid.NewGuid();
        RefundRequest request = RequestedBy(agentId);

        // Act
        Result result = request.Reject(adminId, "The order was delivered and signed for", UtcNow);

        // Assert
        result.IsSuccess.Should().BeTrue();
        request.Status.Should().Be(RefundStatus.Rejected);
        request.DecidedByAdminId.Should().Be(adminId);

        AssertDomainEventWasPublished<RefundRejectedDomainEvent>(request);
    }

    [Fact]
    public void Approve_ShouldFail_WhenTheRequestingAgentDecidesTheirOwnRequest()
    {
        // Arrange — an administrator who also holds refunds:request. The permission set cannot
        // catch this; only the aggregate can.
        var agentId = Guid.NewGuid();
        RefundRequest request = RequestedBy(agentId);

        // Act
        Result result = request.Approve(agentId, note: null, UtcNow);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(RefundErrors.RequesterCannotDecide);
        request.Status.Should().Be(RefundStatus.Requested);
        request.DecidedByAdminId.Should().BeNull();
        request.DomainEvents.Should().BeEmpty();
    }

    [Fact]
    public void Reject_ShouldFail_WhenTheRequestingAgentDecidesTheirOwnRequest()
    {
        // Arrange
        var agentId = Guid.NewGuid();
        RefundRequest request = RequestedBy(agentId);

        // Act
        Result result = request.Reject(agentId, note: null, UtcNow);

        // Assert — the same rule on both verbs: an agent must not be able to close their own
        // request either way.
        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(RefundErrors.RequesterCannotDecide);
        request.Status.Should().Be(RefundStatus.Requested);
        request.DomainEvents.Should().BeEmpty();
    }

    [Fact]
    public void Approve_ShouldFail_WhenTheRequestWasAlreadyApproved()
    {
        // Arrange
        RefundRequest request = RequestedBy(Guid.NewGuid());
        var firstAdminId = Guid.NewGuid();
        request.Approve(firstAdminId, "Agreed", UtcNow);
        request.ClearDomainEvents();

        // Act — a second administrator arrives at a request that has already been decided.
        Result result = request.Approve(Guid.NewGuid(), "Agreed too", UtcNow.AddMinutes(1));

        // Assert — the first decision stands, and no second event goes on the bus.
        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(RefundErrors.AlreadyDecided);
        request.DecidedByAdminId.Should().Be(firstAdminId);
        request.DecidedOnUtc.Should().Be(UtcNow);
        request.DomainEvents.Should().BeEmpty();
    }

    [Fact]
    public void Approve_ShouldFail_WhenTheRequestWasRejected()
    {
        // Arrange
        RefundRequest request = RequestedBy(Guid.NewGuid());
        request.Reject(Guid.NewGuid(), "Delivered and signed for", UtcNow);
        request.ClearDomainEvents();

        // Act — a rejection is a decision, so it closes the request to approval as firmly as an
        // approval closes it to rejection.
        Result result = request.Approve(Guid.NewGuid(), note: null, UtcNow.AddMinutes(1));

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(RefundErrors.AlreadyDecided);
        request.Status.Should().Be(RefundStatus.Rejected);
        request.DomainEvents.Should().BeEmpty();
    }

    [Fact]
    public void Reject_ShouldFail_WhenTheRequestWasAlreadyApproved()
    {
        // Arrange
        RefundRequest request = RequestedBy(Guid.NewGuid());
        request.Approve(Guid.NewGuid(), "Agreed", UtcNow);
        request.ClearDomainEvents();

        // Act
        Result result = request.Reject(Guid.NewGuid(), "Changed my mind", UtcNow.AddMinutes(1));

        // Assert — undoing an approval is not a rejection. Whoever wants that raises a new request.
        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(RefundErrors.AlreadyDecided);
        request.Status.Should().Be(RefundStatus.Approved);
        request.DomainEvents.Should().BeEmpty();
    }

    [Fact]
    public void Decide_ShouldCheckTheRequesterBeforeTheStatus_WhenBothWouldFail()
    {
        // Arrange — a decided request whose decider is also the requester is unreachable in
        // practice; what this pins down is that neither guard mutates before the other has run.
        var agentId = Guid.NewGuid();
        RefundRequest request = RequestedBy(agentId);
        request.Approve(Guid.NewGuid(), "Agreed", UtcNow);
        request.ClearDomainEvents();

        // Act
        Result result = request.Reject(agentId, note: null, UtcNow.AddMinutes(1));

        // Assert — AlreadyDecided wins, because the decision is the fact that has already happened.
        result.Error.Should().Be(RefundErrors.AlreadyDecided);
        request.Status.Should().Be(RefundStatus.Approved);
    }

    [Fact]
    public void Settle_ShouldMoveAnApprovedRequestToSettled()
    {
        // Arrange
        RefundRequest request = Approved();

        // Act
        Result result = request.Settle(UtcNow.AddMinutes(1));

        // Assert
        result.IsSuccess.Should().BeTrue();
        request.Status.Should().Be(RefundStatus.Settled);
        request.SettledOnUtc.Should().Be(UtcNow.AddMinutes(1));
        request.FailureReason.Should().BeNull();
    }

    [Fact]
    public void Settle_ShouldRaiseNoDomainEvent()
    {
        // Arrange
        RefundRequest request = Approved();

        // Act
        request.Settle(UtcNow.AddMinutes(1));

        // Assert — a departure from this codebase's usual rule, argued on the method: nothing
        // consumes it, and the audit entry the handler writes in the same transaction is the record.
        // An event here would be a second, divergent copy of a history this module already keeps.
        request.DomainEvents.Should().BeEmpty();
    }

    [Fact]
    public void Settle_ShouldBeANoOp_OnARequestThatWasNeverApproved()
    {
        // Arrange — Payments only ever refunds against an approval, so this is unreachable in
        // practice. What it pins is that a stray message cannot settle a request nobody agreed to.
        RefundRequest request = RequestedBy(Guid.NewGuid());

        // Act
        Result result = request.Settle(UtcNow.AddMinutes(1));

        // Assert
        result.IsSuccess.Should().BeTrue();
        request.Status.Should().Be(RefundStatus.Requested);
        request.SettledOnUtc.Should().BeNull();
    }

    [Fact]
    public void Settle_ShouldBeANoOp_WhenAlreadySettled()
    {
        // Arrange — the inbox dispatches at least once.
        RefundRequest request = Approved();
        request.Settle(UtcNow.AddMinutes(1));

        // Act
        Result result = request.Settle(UtcNow.AddMinutes(5));

        // Assert — the first settlement's timestamp stands, so the audit trail and the row agree.
        result.IsSuccess.Should().BeTrue();
        request.SettledOnUtc.Should().Be(UtcNow.AddMinutes(1));
    }

    [Fact]
    public void MarkFailed_ShouldRecordTheReasonOnTheRequest()
    {
        // Arrange
        RefundRequest request = Approved();

        // Act
        Result result = request.MarkFailed("not_card_payment", UtcNow.AddMinutes(1));

        // Assert — the reason is on the row as well as in the audit log, because the refund queue is
        // where an agent looks and "failed" on its own is not something anyone can act on.
        result.IsSuccess.Should().BeTrue();
        request.Status.Should().Be(RefundStatus.Failed);
        request.FailureReason.Should().Be("not_card_payment");
        request.FailedOnUtc.Should().Be(UtcNow.AddMinutes(1));
    }

    [Fact]
    public void MarkFailed_ShouldRefuseABlankReason()
    {
        // Arrange
        RefundRequest request = Approved();

        // Act
        Result result = request.MarkFailed("   ", UtcNow.AddMinutes(1));

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(RefundErrors.SettlementReasonRequired);
        request.Status.Should().Be(RefundStatus.Approved);
    }

    [Fact]
    public void MarkFailed_ShouldNotWithdrawTheApproval()
    {
        // Arrange
        var adminId = Guid.NewGuid();
        RefundRequest request = RequestedBy(Guid.NewGuid());
        request.Approve(adminId, "Agreed", UtcNow);
        request.ClearDomainEvents();

        // Act
        request.MarkFailed("payment_not_captured", UtcNow.AddMinutes(1));

        // Assert — the decision trail is untouched. What changed is that the request stops claiming
        // to be in progress; who agreed to it, and when, is still the record.
        request.DecidedByAdminId.Should().Be(adminId);
        request.DecisionNote.Should().Be("Agreed");
        request.DecidedOnUtc.Should().Be(UtcNow);
    }

    [Fact]
    public void MarkFailed_ShouldBeTerminal()
    {
        // Arrange — a failed refund is retried by raising a fresh request, so that the second
        // attempt gets its own administrator rather than riding on the first one's approval.
        RefundRequest request = Approved();
        request.MarkFailed("gateway_error", UtcNow.AddMinutes(1));

        // Act
        Result result = request.Settle(UtcNow.AddMinutes(5));

        // Assert
        result.IsSuccess.Should().BeTrue();
        request.Status.Should().Be(RefundStatus.Failed);
        request.SettledOnUtc.Should().BeNull();
    }
}

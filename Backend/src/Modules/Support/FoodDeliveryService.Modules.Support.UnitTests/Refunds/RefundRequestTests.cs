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
}

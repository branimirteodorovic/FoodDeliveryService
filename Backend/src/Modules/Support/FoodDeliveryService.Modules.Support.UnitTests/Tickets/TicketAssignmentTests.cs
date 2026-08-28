using AwesomeAssertions;
using FoodDeliveryService.Common.Domain;
using FoodDeliveryService.Modules.Support.Domain.Tickets;
using FoodDeliveryService.Modules.Support.UnitTests.Abstractions;

namespace FoodDeliveryService.Modules.Support.UnitTests.Tickets;

/// <summary>
/// Assignment is deliberately not part of the status machine — claiming a ticket says who owns it,
/// not that work has started — so it gets its own table here.
/// <para>
/// Every assertion in this file is about the aggregate guard, which is the rule of record. The
/// distributed lock the handlers take only makes two concurrent callers observe these guards in
/// sequence; the "exactly one wins" property is proven end to end in the integration suite, because
/// nothing in a single-threaded unit test could distinguish a lock that works from one that is not
/// there at all.
/// </para>
/// </summary>
public class TicketAssignmentTests : BaseTest
{
    private const string Reference = "SUP-00000001";

    private static readonly DateTime UtcNow = new(2026, 8, 28, 12, 0, 0, DateTimeKind.Utc);

    private static Ticket CreateOpenTicket()
    {
        Ticket ticket = Ticket.Create(
            Reference,
            Guid.NewGuid(),
            orderId: null,
            Faker.Lorem.Sentence(),
            TicketCategory.Other,
            TicketSource.CustomerPortal,
            UtcNow).Value;

        ticket.ClearDomainEvents();

        return ticket;
    }

    /// <summary>An assigned ticket an agent is actively working.</summary>
    private static Ticket CreateInProgressTicket(Guid agentId)
    {
        Ticket ticket = CreateOpenTicket();
        ticket.Claim(agentId);
        ticket.StartProgress(agentId);
        ticket.ClearDomainEvents();

        return ticket;
    }

    private static Ticket CreateResolvedTicket(Guid agentId)
    {
        Ticket ticket = CreateInProgressTicket(agentId);
        ticket.Resolve(agentId, "Refund issued", UtcNow);
        ticket.ClearDomainEvents();

        return ticket;
    }

    // ---- Claim -----------------------------------------------------------------------------

    [Fact]
    public void Claim_ShouldAssignTheAgent_AndRaiseTicketClaimedDomainEvent()
    {
        // Arrange
        Ticket ticket = CreateOpenTicket();
        var agentId = Guid.NewGuid();

        // Act
        Result result = ticket.Claim(agentId);

        // Assert
        result.IsSuccess.Should().BeTrue();
        ticket.AssignedAgentId.Should().Be(agentId);

        // Untouched on purpose: an agent who has taken a ticket but not started on it is a state the
        // queue needs to be able to represent, so claiming must not imply StartProgress.
        ticket.Status.Should().Be(TicketStatus.Open);

        TicketClaimedDomainEvent domainEvent = AssertDomainEventWasPublished<TicketClaimedDomainEvent>(ticket);

        domainEvent.TicketId.Should().Be(ticket.Id);
        domainEvent.AgentId.Should().Be(agentId);
    }

    [Fact]
    public void Claim_ShouldSucceed_ForAnEscalatedTicket()
    {
        // Arrange — an escalated ticket that was handed back is exactly the case a supervisor picks
        // up out of the queue, so Escalated is claimable alongside Open.
        var firstAgentId = Guid.NewGuid();
        Ticket ticket = CreateOpenTicket();
        ticket.Claim(firstAgentId);
        ticket.Escalate(firstAgentId, "Needs a supervisor");
        ticket.Unassign(firstAgentId, "Handing back to the queue");
        ticket.ClearDomainEvents();

        var supervisorId = Guid.NewGuid();

        // Act
        Result result = ticket.Claim(supervisorId);

        // Assert
        result.IsSuccess.Should().BeTrue();
        ticket.Status.Should().Be(TicketStatus.Escalated);
        ticket.AssignedAgentId.Should().Be(supervisorId);
    }

    [Fact]
    public void Claim_ShouldFail_WhenTheTicketIsAlreadyAssigned()
    {
        // Arrange — the second half of the race the distributed lock serializes. This guard is what
        // actually refuses the loser; the lock only makes sure the loser sees the committed state.
        Ticket ticket = CreateOpenTicket();
        var firstAgentId = Guid.NewGuid();
        ticket.Claim(firstAgentId);
        ticket.ClearDomainEvents();

        // Act
        Result result = ticket.Claim(Guid.NewGuid());

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(TicketErrors.AlreadyAssigned);

        // The first agent keeps the ticket, and no event is raised for the move that did not happen.
        ticket.AssignedAgentId.Should().Be(firstAgentId);
        ticket.DomainEvents.Should().BeEmpty();
    }

    [Theory]
    [InlineData(TicketStatus.InProgress)]
    [InlineData(TicketStatus.Resolved)]
    [InlineData(TicketStatus.Closed)]
    public void Claim_ShouldFail_WhenTheTicketIsNotInTheQueue(TicketStatus status)
    {
        // Arrange
        var agentId = Guid.NewGuid();

        Ticket ticket = status switch
        {
            TicketStatus.InProgress => CreateInProgressTicket(agentId),
            TicketStatus.Resolved => CreateResolvedTicket(agentId),
            _ => CloseTicket(CreateResolvedTicket(agentId), agentId)
        };

        // Unassign it first, so the failure below is about the status and not about the assignee —
        // otherwise AlreadyAssigned would mask the check this test is here to make.
        ticket.SetAssignedAgent(null);
        ticket.ClearDomainEvents();

        // Act
        Result result = ticket.Claim(Guid.NewGuid());

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(TicketErrors.NotClaimable(status));
        ticket.AssignedAgentId.Should().BeNull();
        ticket.DomainEvents.Should().BeEmpty();
    }

    // ---- AssignTo --------------------------------------------------------------------------

    [Fact]
    public void AssignTo_ShouldAssignAnUnassignedTicket_AndRaiseTicketAssignedDomainEvent()
    {
        // Arrange
        Ticket ticket = CreateOpenTicket();
        var agentId = Guid.NewGuid();
        var actorId = Guid.NewGuid();

        // Act
        Result result = ticket.AssignTo(agentId, actorId);

        // Assert
        result.IsSuccess.Should().BeTrue();
        ticket.AssignedAgentId.Should().Be(agentId);

        TicketAssignedDomainEvent domainEvent = AssertDomainEventWasPublished<TicketAssignedDomainEvent>(ticket);

        domainEvent.AgentId.Should().Be(agentId);
        domainEvent.ActorId.Should().Be(actorId);
        domainEvent.PreviousAgentId.Should().BeNull();
    }

    [Fact]
    public void AssignTo_ShouldReassignAnAssignedTicket_AndCarryThePreviousAgent()
    {
        // Arrange — the administrator override, and the one thing Claim cannot do. The outgoing
        // assignee is on the event because otherwise the audit log records only where a ticket
        // ended up, never who it was taken from.
        var firstAgentId = Guid.NewGuid();
        Ticket ticket = CreateInProgressTicket(firstAgentId);

        var secondAgentId = Guid.NewGuid();
        var adminId = Guid.NewGuid();

        // Act
        Result result = ticket.AssignTo(secondAgentId, adminId);

        // Assert
        result.IsSuccess.Should().BeTrue();
        ticket.AssignedAgentId.Should().Be(secondAgentId);

        // Reassignment moves the owner, not the progress.
        ticket.Status.Should().Be(TicketStatus.InProgress);

        TicketAssignedDomainEvent domainEvent = AssertDomainEventWasPublished<TicketAssignedDomainEvent>(ticket);

        domainEvent.PreviousAgentId.Should().Be(firstAgentId);
        domainEvent.ActorId.Should().Be(adminId);
    }

    [Fact]
    public void AssignTo_ShouldFail_WhenTheTicketIsAlreadyAssignedToThatAgent()
    {
        // Arrange — a no-op must raise no event, because an audit entry for an assignment that did
        // not happen is worse than a missing one: it reads as evidence of an action.
        var agentId = Guid.NewGuid();
        Ticket ticket = CreateInProgressTicket(agentId);

        // Act
        Result result = ticket.AssignTo(agentId, Guid.NewGuid());

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(TicketErrors.AlreadyAssignedToAgent);
        ticket.AssignedAgentId.Should().Be(agentId);
        ticket.DomainEvents.Should().BeEmpty();
    }

    [Fact]
    public void AssignTo_ShouldFail_WhenNoAgentIsNamed()
    {
        // Arrange
        Ticket ticket = CreateOpenTicket();

        // Act
        Result result = ticket.AssignTo(Guid.Empty, Guid.NewGuid());

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(TicketErrors.AgentRequired);
        ticket.AssignedAgentId.Should().BeNull();
        ticket.DomainEvents.Should().BeEmpty();
    }

    [Theory]
    [InlineData(TicketStatus.Resolved)]
    [InlineData(TicketStatus.Closed)]
    public void AssignTo_ShouldFail_WhenTheTicketIsNoLongerBeingWorked(TicketStatus status)
    {
        // Arrange — assignment is only meaningful while there is work to own.
        var agentId = Guid.NewGuid();

        Ticket ticket = status == TicketStatus.Resolved
            ? CreateResolvedTicket(agentId)
            : CloseTicket(CreateResolvedTicket(agentId), agentId);

        ticket.ClearDomainEvents();

        // Act
        Result result = ticket.AssignTo(Guid.NewGuid(), Guid.NewGuid());

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(TicketErrors.NotAssignable(status));
        ticket.AssignedAgentId.Should().Be(agentId);
        ticket.DomainEvents.Should().BeEmpty();
    }

    // ---- Unassign --------------------------------------------------------------------------

    [Fact]
    public void Unassign_ShouldClearTheAssignee_AndRaiseTicketUnassignedDomainEvent()
    {
        // Arrange
        var agentId = Guid.NewGuid();
        Ticket ticket = CreateInProgressTicket(agentId);
        var actorId = Guid.NewGuid();

        // Act
        Result result = ticket.Unassign(actorId, "Reassigning to the billing team");

        // Assert
        result.IsSuccess.Should().BeTrue();
        ticket.AssignedAgentId.Should().BeNull();

        TicketUnassignedDomainEvent domainEvent = AssertDomainEventWasPublished<TicketUnassignedDomainEvent>(ticket);

        domainEvent.ActorId.Should().Be(actorId);
        domainEvent.PreviousAgentId.Should().Be(agentId);
        domainEvent.Reason.Should().Be("Reassigning to the billing team");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Unassign_ShouldFail_WithoutAReason(string? reason)
    {
        // Arrange — the one assignment action whose motive cannot be read off the outcome, which is
        // why the aggregate refuses it rather than leaving it to the validator.
        var agentId = Guid.NewGuid();
        Ticket ticket = CreateInProgressTicket(agentId);

        // Act
        Result result = ticket.Unassign(Guid.NewGuid(), reason!);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(TicketErrors.UnassignReasonRequired);
        ticket.AssignedAgentId.Should().Be(agentId);
        ticket.DomainEvents.Should().BeEmpty();
    }

    [Fact]
    public void Unassign_ShouldFail_WhenNobodyIsAssigned()
    {
        // Arrange
        Ticket ticket = CreateOpenTicket();

        // Act
        Result result = ticket.Unassign(Guid.NewGuid(), "Nobody to hand back");

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(TicketErrors.NotAssigned);
        ticket.DomainEvents.Should().BeEmpty();
    }

    [Fact]
    public void Unassign_ShouldFail_ForAClosedTicket()
    {
        // Arrange
        var agentId = Guid.NewGuid();
        Ticket ticket = CloseTicket(CreateResolvedTicket(agentId), agentId);
        ticket.ClearDomainEvents();

        // Act
        Result result = ticket.Unassign(Guid.NewGuid(), "Too late");

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(TicketErrors.NotAssignable(TicketStatus.Closed));
        ticket.AssignedAgentId.Should().Be(agentId);
        ticket.DomainEvents.Should().BeEmpty();
    }

    [Fact]
    public void Unassign_ShouldReturnTheTicketToTheQueue()
    {
        // Arrange — the round trip that makes a hand-back useful: an unassigned ticket is claimable
        // again, which is the whole point of putting it back rather than reassigning it directly.
        var firstAgentId = Guid.NewGuid();
        Ticket ticket = CreateOpenTicket();
        ticket.Claim(firstAgentId);
        ticket.Unassign(firstAgentId, "Not my area");
        ticket.ClearDomainEvents();

        var secondAgentId = Guid.NewGuid();

        // Act
        Result result = ticket.Claim(secondAgentId);

        // Assert
        result.IsSuccess.Should().BeTrue();
        ticket.AssignedAgentId.Should().Be(secondAgentId);
    }

    private static Ticket CloseTicket(Ticket ticket, Guid actorId)
    {
        ticket.Close(actorId, UtcNow);

        return ticket;
    }
}

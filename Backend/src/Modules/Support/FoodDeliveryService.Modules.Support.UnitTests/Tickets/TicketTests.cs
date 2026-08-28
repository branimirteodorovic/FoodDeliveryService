using AwesomeAssertions;
using FoodDeliveryService.Common.Domain;
using FoodDeliveryService.Modules.Support.Domain.Tickets;
using FoodDeliveryService.Modules.Support.UnitTests.Abstractions;

namespace FoodDeliveryService.Modules.Support.UnitTests.Tickets;

/// <summary>
/// The state machine is the whole domain of this feature, so this suite is the transition table:
/// every legal move, every illegal one and the error it returns, and — the case that actually
/// matters for the outbox — that a rejected transition raises no domain event at all.
/// </summary>
public class TicketTests : BaseTest
{
    private const string Reference = "SUP-00000001";

    private static readonly DateTime UtcNow = new(2026, 8, 19, 12, 0, 0, DateTimeKind.Utc);

    private static Ticket CreateTicket(
        TicketCategory category = TicketCategory.Other,
        TicketSource source = TicketSource.CustomerPortal,
        Guid? customerId = null,
        Guid? orderId = null)
    {
        return Ticket.Create(
            Reference,
            customerId ?? Guid.NewGuid(),
            orderId,
            Faker.Lorem.Sentence(),
            category,
            source,
            UtcNow).Value;
    }

    /// <summary>
    /// Puts a ticket into InProgress the way the assignment milestone will: an agent is put on it,
    /// then starts work. SetAssignedAgent is the internal seam Claim/AssignTo will wrap.
    /// </summary>
    private static Ticket CreateInProgressTicket(Guid agentId)
    {
        Ticket ticket = CreateTicket();
        ticket.SetAssignedAgent(agentId);
        ticket.StartProgress(agentId);
        ticket.ClearDomainEvents();

        return ticket;
    }

    private static Ticket CreateResolvedTicket(Guid agentId, DateTime? resolvedOnUtc = null)
    {
        Ticket ticket = CreateInProgressTicket(agentId);
        ticket.Resolve(agentId, "Refund issued", resolvedOnUtc ?? UtcNow);
        ticket.ClearDomainEvents();

        return ticket;
    }

    private static Ticket CreateEscalatedTicket(Guid agentId)
    {
        Ticket ticket = CreateTicket();
        ticket.SetAssignedAgent(agentId);
        ticket.Escalate(agentId, "Needs a supervisor");
        ticket.ClearDomainEvents();

        return ticket;
    }

    [Fact]
    public void Create_ShouldOpenUnassignedAtNormalPriority_AndRaiseTicketOpenedDomainEvent()
    {
        // Arrange
        var customerId = Guid.NewGuid();
        var orderId = Guid.NewGuid();

        // Act
        Ticket ticket = CreateTicket(customerId: customerId, orderId: orderId);

        // Assert
        ticket.Status.Should().Be(TicketStatus.Open);
        ticket.Priority.Should().Be(TicketPriority.Normal);
        ticket.AssignedAgentId.Should().BeNull();
        ticket.Reference.Should().Be(Reference);
        ticket.CustomerId.Should().Be(customerId);
        ticket.OrderId.Should().Be(orderId);
        ticket.OpenedOnUtc.Should().Be(UtcNow);
        ticket.ResolvedOnUtc.Should().BeNull();
        ticket.ClosedOnUtc.Should().BeNull();

        // Reserved for the AI assistant; nothing in this feature writes it.
        ticket.EscalationTranscript.Should().BeNull();

        TicketOpenedDomainEvent domainEvent = AssertDomainEventWasPublished<TicketOpenedDomainEvent>(ticket);

        domainEvent.TicketId.Should().Be(ticket.Id);
        domainEvent.Reference.Should().Be(Reference);
        domainEvent.CustomerId.Should().Be(customerId);
        domainEvent.OrderId.Should().Be(orderId);
        domainEvent.Priority.Should().Be(TicketPriority.Normal);
        domainEvent.Source.Should().Be(TicketSource.CustomerPortal);
        domainEvent.OpenedOnUtc.Should().Be(UtcNow);
    }

    [Fact]
    public void Create_ShouldOpenAtHighPriority_WhenCategoryIsOrderNotReceived()
    {
        // Act
        Ticket ticket = CreateTicket(TicketCategory.OrderNotReceived);

        // Assert
        ticket.Priority.Should().Be(TicketPriority.High);
        AssertDomainEventWasPublished<TicketOpenedDomainEvent>(ticket).Priority.Should().Be(TicketPriority.High);
    }

    [Theory]
    [InlineData(TicketCategory.ItemMissing)]
    [InlineData(TicketCategory.FoodQuality)]
    [InlineData(TicketCategory.DriverIssue)]
    [InlineData(TicketCategory.PaymentIssue)]
    [InlineData(TicketCategory.AppIssue)]
    [InlineData(TicketCategory.Other)]
    public void Create_ShouldOpenAtNormalPriority_ForEveryOtherCategory(TicketCategory category)
    {
        // Act
        Ticket ticket = CreateTicket(category);

        // Assert
        ticket.Priority.Should().Be(TicketPriority.Normal);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_ShouldFail_WhenSubjectIsEmpty(string subject)
    {
        // Act
        Result<Ticket> result = Ticket.Create(
            Reference,
            Guid.NewGuid(),
            orderId: null,
            subject,
            TicketCategory.Other,
            TicketSource.CustomerPortal,
            UtcNow);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(TicketErrors.SubjectRequired);
    }

    [Fact]
    public void Create_ShouldFail_WhenSubjectExceedsTheMaximumLength()
    {
        // Act
        Result<Ticket> result = Ticket.Create(
            Reference,
            Guid.NewGuid(),
            orderId: null,
            new string('x', Ticket.SubjectMaxLength + 1),
            TicketCategory.Other,
            TicketSource.CustomerPortal,
            UtcNow);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(TicketErrors.SubjectTooLong);
    }

    [Fact]
    public void Create_ShouldAcceptASubjectExactlyAtTheMaximumLength()
    {
        // Act
        Result<Ticket> result = Ticket.Create(
            Reference,
            Guid.NewGuid(),
            orderId: null,
            new string('x', Ticket.SubjectMaxLength),
            TicketCategory.Other,
            TicketSource.CustomerPortal,
            UtcNow);

        // Assert
        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void StartProgress_ShouldMoveToInProgress_AndRaiseTicketProgressStartedDomainEvent()
    {
        // Arrange
        var agentId = Guid.NewGuid();
        Ticket ticket = CreateTicket();
        ticket.SetAssignedAgent(agentId);
        ticket.ClearDomainEvents();

        // Act
        Result result = ticket.StartProgress(agentId);

        // Assert
        result.IsSuccess.Should().BeTrue();
        ticket.Status.Should().Be(TicketStatus.InProgress);

        AssertDomainEventWasPublished<TicketProgressStartedDomainEvent>(ticket).AgentId.Should().Be(agentId);
    }

    [Fact]
    public void StartProgress_ShouldResumeAnEscalatedTicket()
    {
        // Arrange
        var agentId = Guid.NewGuid();
        Ticket ticket = CreateEscalatedTicket(agentId);

        // Act
        Result result = ticket.StartProgress(agentId);

        // Assert
        result.IsSuccess.Should().BeTrue();
        ticket.Status.Should().Be(TicketStatus.InProgress);
    }

    [Fact]
    public void StartProgress_ShouldFail_WhenNobodyIsAssigned()
    {
        // Arrange
        Ticket ticket = CreateTicket();
        ticket.ClearDomainEvents();

        // Act
        Result result = ticket.StartProgress(Guid.NewGuid());

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(TicketErrors.NotAssigned);
        ticket.Status.Should().Be(TicketStatus.Open);
        ticket.DomainEvents.Should().BeEmpty();
    }

    [Fact]
    public void StartProgress_ShouldFail_AndRaiseNothing_WhenTheTicketIsAlreadyInProgress()
    {
        // Arrange
        var agentId = Guid.NewGuid();
        Ticket ticket = CreateInProgressTicket(agentId);

        // Act
        Result result = ticket.StartProgress(agentId);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(TicketErrors.InvalidTransition(TicketStatus.InProgress, TicketStatus.InProgress));
        ticket.DomainEvents.Should().BeEmpty();
    }

    [Fact]
    public void Resolve_ShouldStampResolvedOnUtc_AndRaiseTicketResolvedDomainEvent()
    {
        // Arrange
        var agentId = Guid.NewGuid();
        Ticket ticket = CreateInProgressTicket(agentId);
        DateTime resolvedOnUtc = UtcNow.AddHours(3);

        // Act
        Result result = ticket.Resolve(agentId, "Refund issued", resolvedOnUtc);

        // Assert
        result.IsSuccess.Should().BeTrue();
        ticket.Status.Should().Be(TicketStatus.Resolved);
        ticket.ResolvedOnUtc.Should().Be(resolvedOnUtc);

        TicketResolvedDomainEvent domainEvent = AssertDomainEventWasPublished<TicketResolvedDomainEvent>(ticket);

        domainEvent.AgentId.Should().Be(agentId);
        domainEvent.Resolution.Should().Be("Refund issued");

        // Both ends of the interval travel with the event, so a consumer can compute the
        // resolution time without ever querying Support.
        domainEvent.OpenedOnUtc.Should().Be(UtcNow);
        domainEvent.ResolvedOnUtc.Should().Be(resolvedOnUtc);
    }

    [Fact]
    public void Resolve_ShouldSucceedFromEscalated()
    {
        // Arrange
        var agentId = Guid.NewGuid();
        Ticket ticket = CreateEscalatedTicket(agentId);

        // Act
        Result result = ticket.Resolve(agentId, "Supervisor approved a credit", UtcNow);

        // Assert
        result.IsSuccess.Should().BeTrue();
        ticket.Status.Should().Be(TicketStatus.Resolved);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Resolve_ShouldFail_AndRaiseNothing_WhenTheResolutionNoteIsEmpty(string resolution)
    {
        // Arrange
        var agentId = Guid.NewGuid();
        Ticket ticket = CreateInProgressTicket(agentId);

        // Act
        Result result = ticket.Resolve(agentId, resolution, UtcNow);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(TicketErrors.ResolutionRequired);
        ticket.Status.Should().Be(TicketStatus.InProgress);
        ticket.ResolvedOnUtc.Should().BeNull();
        ticket.DomainEvents.Should().BeEmpty();
    }

    [Fact]
    public void Resolve_ShouldFail_AndRaiseNothing_WhenTheTicketIsAlreadyResolved()
    {
        // Arrange
        var agentId = Guid.NewGuid();
        Ticket ticket = CreateResolvedTicket(agentId);

        // Act
        Result result = ticket.Resolve(agentId, "Refund issued again", UtcNow);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(TicketErrors.InvalidTransition(TicketStatus.Resolved, TicketStatus.Resolved));

        // The point of the no-op case: a redundant resolve must not put a second
        // SupportTicketResolved message on the bus.
        ticket.DomainEvents.Should().BeEmpty();
    }

    [Fact]
    public void Resolve_ShouldFail_WhenTheTicketIsStillOpen()
    {
        // Arrange
        var agentId = Guid.NewGuid();
        Ticket ticket = CreateTicket();
        ticket.SetAssignedAgent(agentId);
        ticket.ClearDomainEvents();

        // Act
        Result result = ticket.Resolve(agentId, "Refund issued", UtcNow);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(TicketErrors.InvalidTransition(TicketStatus.Open, TicketStatus.Resolved));
        ticket.DomainEvents.Should().BeEmpty();
    }

    [Fact]
    public void Escalate_ShouldMoveToEscalated_AndKeepTheCurrentAssignee()
    {
        // Arrange
        var agentId = Guid.NewGuid();
        Ticket ticket = CreateInProgressTicket(agentId);

        // Act
        Result result = ticket.Escalate(agentId, "Needs a supervisor");

        // Assert
        result.IsSuccess.Should().BeTrue();
        ticket.Status.Should().Be(TicketStatus.Escalated);

        // Escalation asks for help; it does not return the ticket to the queue.
        ticket.AssignedAgentId.Should().Be(agentId);

        TicketEscalatedDomainEvent domainEvent = AssertDomainEventWasPublished<TicketEscalatedDomainEvent>(ticket);

        domainEvent.AgentId.Should().Be(agentId);
        domainEvent.Reason.Should().Be("Needs a supervisor");
    }

    [Fact]
    public void Escalate_ShouldSucceedFromOpen_WithoutAnAssignee()
    {
        // Arrange — the one agent transition this milestone can perform end to end, because it is
        // the only one whose meaning does not depend on somebody owning the ticket.
        Ticket ticket = CreateTicket();
        ticket.ClearDomainEvents();

        // Act
        Result result = ticket.Escalate(Guid.NewGuid(), "No agent available for a High priority case");

        // Assert
        result.IsSuccess.Should().BeTrue();
        ticket.Status.Should().Be(TicketStatus.Escalated);
        ticket.AssignedAgentId.Should().BeNull();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Escalate_ShouldFail_AndRaiseNothing_WhenTheReasonIsEmpty(string reason)
    {
        // Arrange
        Ticket ticket = CreateTicket();
        ticket.ClearDomainEvents();

        // Act
        Result result = ticket.Escalate(Guid.NewGuid(), reason);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(TicketErrors.EscalationReasonRequired);
        ticket.Status.Should().Be(TicketStatus.Open);
        ticket.DomainEvents.Should().BeEmpty();
    }

    [Fact]
    public void Escalate_ShouldFail_AndRaiseNothing_WhenTheTicketIsAlreadyEscalated()
    {
        // Arrange
        Ticket ticket = CreateEscalatedTicket(Guid.NewGuid());

        // Act
        Result result = ticket.Escalate(Guid.NewGuid(), "Again");

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(TicketErrors.InvalidTransition(TicketStatus.Escalated, TicketStatus.Escalated));
        ticket.DomainEvents.Should().BeEmpty();
    }

    [Fact]
    public void Escalate_ShouldFail_WhenTheTicketIsResolved()
    {
        // Arrange
        Ticket ticket = CreateResolvedTicket(Guid.NewGuid());

        // Act
        Result result = ticket.Escalate(Guid.NewGuid(), "Too late");

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(TicketErrors.InvalidTransition(TicketStatus.Resolved, TicketStatus.Escalated));
    }

    [Fact]
    public void Reopen_ShouldReturnToInProgress_AndClearResolvedOnUtc()
    {
        // Arrange
        Ticket ticket = CreateResolvedTicket(Guid.NewGuid());
        var actorId = Guid.NewGuid();

        // Act
        Result result = ticket.Reopen(actorId, UtcNow.AddDays(1));

        // Assert
        result.IsSuccess.Should().BeTrue();
        ticket.Status.Should().Be(TicketStatus.InProgress);

        // Cleared so the analytics numerator stops counting a resolution that was undone.
        ticket.ResolvedOnUtc.Should().BeNull();

        AssertDomainEventWasPublished<TicketReopenedDomainEvent>(ticket).ActorId.Should().Be(actorId);
    }

    [Fact]
    public void Reopen_ShouldSucceedOnTheLastDayOfTheWindow()
    {
        // Arrange
        Ticket ticket = CreateResolvedTicket(Guid.NewGuid());

        // Act
        Result result = ticket.Reopen(Guid.NewGuid(), UtcNow.AddDays(Ticket.ReopenWindowInDays));

        // Assert
        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void Reopen_ShouldFail_AndRaiseNothing_OutsideTheReopenWindow()
    {
        // Arrange
        Ticket ticket = CreateResolvedTicket(Guid.NewGuid());

        // Act
        Result result = ticket.Reopen(Guid.NewGuid(), UtcNow.AddDays(Ticket.ReopenWindowInDays).AddSeconds(1));

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(TicketErrors.ReopenWindowElapsed);
        ticket.Status.Should().Be(TicketStatus.Resolved);
        ticket.ResolvedOnUtc.Should().Be(UtcNow);
        ticket.DomainEvents.Should().BeEmpty();
    }

    [Fact]
    public void Reopen_ShouldFail_WhenTheTicketWasNeverResolved()
    {
        // Arrange
        Ticket ticket = CreateInProgressTicket(Guid.NewGuid());

        // Act
        Result result = ticket.Reopen(Guid.NewGuid(), UtcNow);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(TicketErrors.InvalidTransition(TicketStatus.InProgress, TicketStatus.InProgress));
        ticket.DomainEvents.Should().BeEmpty();
    }

    [Fact]
    public void Close_ShouldStampClosedOnUtc_AndRaiseTicketClosedDomainEvent()
    {
        // Arrange
        Ticket ticket = CreateResolvedTicket(Guid.NewGuid());
        var actorId = Guid.NewGuid();
        DateTime closedOnUtc = UtcNow.AddDays(2);

        // Act
        Result result = ticket.Close(actorId, closedOnUtc);

        // Assert
        result.IsSuccess.Should().BeTrue();
        ticket.Status.Should().Be(TicketStatus.Closed);
        ticket.ClosedOnUtc.Should().Be(closedOnUtc);

        TicketClosedDomainEvent domainEvent = AssertDomainEventWasPublished<TicketClosedDomainEvent>(ticket);

        domainEvent.ActorId.Should().Be(actorId);
        domainEvent.ClosedOnUtc.Should().Be(closedOnUtc);
    }

    [Fact]
    public void Close_ShouldFail_WhenTheTicketIsNotResolvedYet()
    {
        // Arrange
        Ticket ticket = CreateInProgressTicket(Guid.NewGuid());

        // Act
        Result result = ticket.Close(Guid.NewGuid(), UtcNow);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(TicketErrors.InvalidTransition(TicketStatus.InProgress, TicketStatus.Closed));
        ticket.DomainEvents.Should().BeEmpty();
    }

    [Fact]
    public void Closed_ShouldBeTerminal_ForEveryTransition()
    {
        // Arrange
        var agentId = Guid.NewGuid();
        Ticket ticket = CreateResolvedTicket(agentId);
        ticket.Close(agentId, UtcNow);
        ticket.ClearDomainEvents();

        // Act
        Result[] results =
        [
            ticket.StartProgress(agentId),
            ticket.Resolve(agentId, "Anything", UtcNow),
            ticket.Escalate(agentId, "Anything"),
            ticket.Reopen(agentId, UtcNow),
            ticket.Close(agentId, UtcNow)
        ];

        // Assert
        results.Should().OnlyContain(r => r.IsFailure);
        ticket.Status.Should().Be(TicketStatus.Closed);
        ticket.DomainEvents.Should().BeEmpty();
    }
}

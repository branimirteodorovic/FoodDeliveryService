using AwesomeAssertions;
using FoodDeliveryService.Common.Domain;
using FoodDeliveryService.Modules.Support.Domain.Tickets;
using FoodDeliveryService.Modules.Support.UnitTests.Abstractions;

namespace FoodDeliveryService.Modules.Support.UnitTests.Tickets;

/// <summary>
/// The message thread's rules, which are the ones with a blast radius outside the state machine: an
/// internal note the wrong person can write is a disclosure, and a first-response timestamp that any
/// message moves is a service-level number that measures nothing.
/// </summary>
public class TicketMessageTests : BaseTest
{
    private const string Reference = "SUP-00000001";

    private static readonly DateTime UtcNow = new(2026, 8, 31, 12, 0, 0, DateTimeKind.Utc);

    private static Ticket CreateTicket(Guid? customerId = null) =>
        Ticket.Create(
            Reference,
            customerId ?? Guid.NewGuid(),
            orderId: null,
            Faker.Lorem.Sentence(),
            TicketCategory.Other,
            TicketSource.CustomerPortal,
            UtcNow).Value;

    private static Ticket CreateInProgressTicket(Guid agentId)
    {
        Ticket ticket = CreateTicket();
        ticket.SetAssignedAgent(agentId);
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

    private static Ticket CreateClosedTicket(Guid agentId)
    {
        Ticket ticket = CreateResolvedTicket(agentId);
        ticket.Close(agentId, UtcNow);
        ticket.ClearDomainEvents();

        return ticket;
    }

    [Fact]
    public void PostMessage_ShouldAppendToThread_AndRaiseTicketMessagePostedDomainEvent()
    {
        // Arrange
        var customerId = Guid.NewGuid();
        Ticket ticket = CreateTicket(customerId);
        ticket.ClearDomainEvents();

        // Act
        Result<TicketMessage> result = ticket.PostMessage(
            customerId,
            TicketAuthorKind.Customer,
            "My order never arrived",
            TicketMessageVisibility.CustomerVisible,
            UtcNow);

        // Assert
        result.IsSuccess.Should().BeTrue();
        ticket.Messages.Should().ContainSingle();

        TicketMessagePostedDomainEvent domainEvent =
            AssertDomainEventWasPublished<TicketMessagePostedDomainEvent>(ticket);

        domainEvent.TicketId.Should().Be(ticket.Id);
        domainEvent.MessageId.Should().Be(result.Value.Id);

        // The recipient of any downstream notification is the ticket's owner, not the author, so the
        // event carries the customer id even when the customer is the one who wrote it.
        domainEvent.CustomerId.Should().Be(customerId);
        domainEvent.AuthorKind.Should().Be(TicketAuthorKind.Customer);
        domainEvent.Visibility.Should().Be(TicketMessageVisibility.CustomerVisible);
        domainEvent.Body.Should().Be("My order never arrived");
    }

    [Fact]
    public void PostMessage_ShouldFail_WhenCustomerPostsInternalNote()
    {
        // Arrange - the rule lives in the aggregate because a customer-authored internal note is a
        // data-integrity bug, not an authorization one: it would look like agent commentary forever.
        var customerId = Guid.NewGuid();
        Ticket ticket = CreateTicket(customerId);
        ticket.ClearDomainEvents();

        // Act
        Result<TicketMessage> result = ticket.PostMessage(
            customerId,
            TicketAuthorKind.Customer,
            "Not for the customer",
            TicketMessageVisibility.InternalNote,
            UtcNow);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(TicketErrors.CustomerCannotPostInternalNote);
        ticket.Messages.Should().BeEmpty();
        ticket.DomainEvents.Should().BeEmpty("a refused message must not put anything on the outbox");
    }

    [Fact]
    public void PostMessage_ShouldFail_WhenTicketIsClosed()
    {
        // Arrange
        var agentId = Guid.NewGuid();
        Ticket ticket = CreateClosedTicket(agentId);

        // Act
        Result<TicketMessage> result = ticket.PostMessage(
            agentId,
            TicketAuthorKind.Agent,
            "One more thing",
            TicketMessageVisibility.CustomerVisible,
            UtcNow);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(TicketErrors.ClosedToMessages);
        ticket.Messages.Should().BeEmpty();
        ticket.DomainEvents.Should().BeEmpty();
    }

    [Fact]
    public void PostMessage_ShouldReturnResolvedTicketToInProgress_AndClearResolvedOnUtc()
    {
        // Arrange - a resolved ticket accepts messages: that is how a customer says the fix did not
        // hold, and refusing it would lose the message along with the conversation.
        var agentId = Guid.NewGuid();
        Ticket ticket = CreateResolvedTicket(agentId);

        // Act
        Result<TicketMessage> result = ticket.PostMessage(
            ticket.CustomerId,
            TicketAuthorKind.Customer,
            "It happened again",
            TicketMessageVisibility.CustomerVisible,
            UtcNow.AddDays(1));

        // Assert
        result.IsSuccess.Should().BeTrue();
        ticket.Status.Should().Be(TicketStatus.InProgress);

        // Cleared for the same reason Reopen clears it: a ticket under active discussion must not
        // keep counting as resolved in the average-resolution-time numerator.
        ticket.ResolvedOnUtc.Should().BeNull();

        AssertDomainEventWasPublished<TicketReopenedDomainEvent>(ticket);
        AssertDomainEventWasPublished<TicketMessagePostedDomainEvent>(ticket);
    }

    [Fact]
    public void PostMessage_ShouldNotStampFirstResponse_WhenPostedByCustomer()
    {
        // Arrange
        var customerId = Guid.NewGuid();
        Ticket ticket = CreateTicket(customerId);

        // Act
        ticket.PostMessage(
            customerId,
            TicketAuthorKind.Customer,
            "Any update?",
            TicketMessageVisibility.CustomerVisible,
            UtcNow);

        // Assert
        ticket.FirstRespondedOnUtc.Should().BeNull("the customer writing is not a response to them");
    }

    [Fact]
    public void PostMessage_ShouldNotStampFirstResponse_WhenAgentPostsInternalNote()
    {
        // Arrange
        var agentId = Guid.NewGuid();
        Ticket ticket = CreateInProgressTicket(agentId);

        // Act
        ticket.PostMessage(
            agentId,
            TicketAuthorKind.Agent,
            "Checking with the restaurant",
            TicketMessageVisibility.InternalNote,
            UtcNow);

        // Assert - agents talking to each other is not a reply; counting it would make the
        // first-response metric satisfiable without anybody ever contacting the customer.
        ticket.FirstRespondedOnUtc.Should().BeNull();
    }

    [Fact]
    public void PostMessage_ShouldStampFirstResponseOnce_WhenAgentRepliesTwice()
    {
        // Arrange
        var agentId = Guid.NewGuid();
        Ticket ticket = CreateInProgressTicket(agentId);

        DateTime firstReply = UtcNow;
        DateTime secondReply = UtcNow.AddHours(3);

        // Act
        ticket.PostMessage(
            agentId,
            TicketAuthorKind.Agent,
            "Looking into it now",
            TicketMessageVisibility.CustomerVisible,
            firstReply);

        ticket.PostMessage(
            agentId,
            TicketAuthorKind.Agent,
            "Refund issued",
            TicketMessageVisibility.CustomerVisible,
            secondReply);

        // Assert - first response means the first one; a later reply must not move it forward.
        ticket.FirstRespondedOnUtc.Should().Be(firstReply);
        ticket.Messages.Should().HaveCount(2);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void PostMessage_ShouldFail_WhenBodyIsEmpty(string body)
    {
        // Arrange
        var agentId = Guid.NewGuid();
        Ticket ticket = CreateInProgressTicket(agentId);

        // Act
        Result<TicketMessage> result = ticket.PostMessage(
            agentId,
            TicketAuthorKind.Agent,
            body,
            TicketMessageVisibility.CustomerVisible,
            UtcNow);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(TicketErrors.MessageBodyRequired);
    }

    [Fact]
    public void PostMessage_ShouldFail_WhenBodyExceedsMaxLength()
    {
        // Arrange
        var agentId = Guid.NewGuid();
        Ticket ticket = CreateInProgressTicket(agentId);

        // Act
        Result<TicketMessage> result = ticket.PostMessage(
            agentId,
            TicketAuthorKind.Agent,
            new string('a', TicketMessage.BodyMaxLength + 1),
            TicketMessageVisibility.CustomerVisible,
            UtcNow);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(TicketErrors.MessageBodyTooLong);
    }
}

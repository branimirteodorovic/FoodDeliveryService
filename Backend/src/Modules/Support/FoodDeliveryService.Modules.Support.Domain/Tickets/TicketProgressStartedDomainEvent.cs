using FoodDeliveryService.Common.Domain;

namespace FoodDeliveryService.Modules.Support.Domain.Tickets;

// PreviousStatus is the status the ticket moved OUT of, and only the aggregate knows it: by the
// time a handler sees the event the ticket has already advanced. Work can start from Open or from
// Escalated, so it genuinely varies — it is what gives the support.tickets.transition counter an
// honest `from` tag rather than one hard-coded from the transition table.
public sealed class TicketProgressStartedDomainEvent(
    Guid ticketId,
    Guid agentId,
    TicketStatus previousStatus) : DomainEvent
{
    public Guid TicketId { get; init; } = ticketId;

    public Guid AgentId { get; init; } = agentId;

    public TicketStatus PreviousStatus { get; init; } = previousStatus;
}

using FoodDeliveryService.Common.Domain;

namespace FoodDeliveryService.Modules.Support.Domain.Tickets;

// PreviousStatus: escalation is reachable from Open and from InProgress, and telling the two apart
// is the point of the tag — a queue that escalates before anybody looks at it is a different
// problem from one that escalates after an agent has tried.
public sealed class TicketEscalatedDomainEvent(
    Guid ticketId,
    Guid agentId,
    string reason,
    TicketStatus previousStatus) : DomainEvent
{
    public Guid TicketId { get; init; } = ticketId;

    public Guid AgentId { get; init; } = agentId;

    public string Reason { get; init; } = reason;

    public TicketStatus PreviousStatus { get; init; } = previousStatus;
}

using FoodDeliveryService.Common.Domain;

namespace FoodDeliveryService.Modules.Support.Domain.Tickets;

/// <summary>
/// An agent took an unassigned ticket out of the queue for themselves. Distinct from
/// <see cref="TicketAssignedDomainEvent"/> on purpose: a claim has no third party, so the actor and
/// the assignee are the same person and the audit log records it as a different kind of action.
/// </summary>
public sealed class TicketClaimedDomainEvent(Guid ticketId, Guid agentId) : DomainEvent
{
    public Guid TicketId { get; init; } = ticketId;

    public Guid AgentId { get; init; } = agentId;
}

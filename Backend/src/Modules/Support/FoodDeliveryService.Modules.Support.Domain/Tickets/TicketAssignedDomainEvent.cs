using FoodDeliveryService.Common.Domain;

namespace FoodDeliveryService.Modules.Support.Domain.Tickets;

/// <summary>
/// A ticket was assigned to an agent by somebody else — an administrator routing work, or an agent
/// taking a ticket through the assign endpoint rather than the queue.
/// <para>
/// <see cref="PreviousAgentId"/> is what makes a reassignment auditable: without the outgoing
/// assignee the log records only where the ticket ended up, not who it was taken from.
/// </para>
/// </summary>
public sealed class TicketAssignedDomainEvent(
    Guid ticketId,
    Guid agentId,
    Guid actorId,
    Guid? previousAgentId) : DomainEvent
{
    public Guid TicketId { get; init; } = ticketId;

    public Guid AgentId { get; init; } = agentId;

    public Guid ActorId { get; init; } = actorId;

    public Guid? PreviousAgentId { get; init; } = previousAgentId;
}

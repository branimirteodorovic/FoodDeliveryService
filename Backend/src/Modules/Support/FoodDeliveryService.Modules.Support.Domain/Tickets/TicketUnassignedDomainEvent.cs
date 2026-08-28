using FoodDeliveryService.Common.Domain;

namespace FoodDeliveryService.Modules.Support.Domain.Tickets;

/// <summary>
/// A ticket was put back in the queue. The reason is mandatory upstream in the aggregate: an
/// unassignment with no explanation is the one audit entry that tells a reviewer nothing.
/// </summary>
public sealed class TicketUnassignedDomainEvent(
    Guid ticketId,
    Guid actorId,
    Guid previousAgentId,
    string reason) : DomainEvent
{
    public Guid TicketId { get; init; } = ticketId;

    public Guid ActorId { get; init; } = actorId;

    public Guid PreviousAgentId { get; init; } = previousAgentId;

    public string Reason { get; init; } = reason;
}

using FoodDeliveryService.Common.Domain;

namespace FoodDeliveryService.Modules.Support.Domain.Tickets;

public sealed class TicketClosedDomainEvent(
    Guid ticketId,
    Guid actorId,
    DateTime closedOnUtc,
    TicketStatus previousStatus) : DomainEvent
{
    public Guid TicketId { get; init; } = ticketId;

    public Guid ActorId { get; init; } = actorId;

    public DateTime ClosedOnUtc { get; init; } = closedOnUtc;

    public TicketStatus PreviousStatus { get; init; } = previousStatus;
}

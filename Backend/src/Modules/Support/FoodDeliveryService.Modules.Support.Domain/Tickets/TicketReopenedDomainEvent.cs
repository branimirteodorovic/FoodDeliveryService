using FoodDeliveryService.Common.Domain;

namespace FoodDeliveryService.Modules.Support.Domain.Tickets;

public sealed class TicketReopenedDomainEvent(Guid ticketId, Guid actorId) : DomainEvent
{
    public Guid TicketId { get; init; } = ticketId;

    public Guid ActorId { get; init; } = actorId;
}

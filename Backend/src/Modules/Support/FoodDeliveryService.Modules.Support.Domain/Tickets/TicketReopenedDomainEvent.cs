using FoodDeliveryService.Common.Domain;

namespace FoodDeliveryService.Modules.Support.Domain.Tickets;

// PreviousStatus is always Resolved today — both roads to a reopen start there — and it is carried
// all the same so the transition counter reads the tag off the event like every other transition
// rather than special-casing this one.
public sealed class TicketReopenedDomainEvent(
    Guid ticketId,
    Guid actorId,
    TicketStatus previousStatus) : DomainEvent
{
    public Guid TicketId { get; init; } = ticketId;

    public Guid ActorId { get; init; } = actorId;

    public TicketStatus PreviousStatus { get; init; } = previousStatus;
}

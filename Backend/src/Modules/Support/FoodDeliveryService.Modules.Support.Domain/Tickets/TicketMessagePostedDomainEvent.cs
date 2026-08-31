using FoodDeliveryService.Common.Domain;

namespace FoodDeliveryService.Modules.Support.Domain.Tickets;

/// <summary>
/// A message was added to a ticket's thread. Carries the author kind and the visibility because the
/// handler needs both to decide whether anything leaves this service at all: only a customer-visible
/// message written by an agent becomes an integration event. An internal note raises this event too
/// — the module's own subscribers may care — and is dropped before the bus.
/// <para>
/// The body travels on it in full. Truncation to a preview happens where the event crosses the
/// module boundary, so the decision about how much of a support conversation leaves the building is
/// made in one place rather than being baked into the domain record.
/// </para>
/// </summary>
public sealed class TicketMessagePostedDomainEvent(
    Guid ticketId,
    string reference,
    Guid messageId,
    Guid customerId,
    Guid authorId,
    TicketAuthorKind authorKind,
    TicketMessageVisibility visibility,
    string subject,
    string body,
    DateTime postedOnUtc) : DomainEvent
{
    public Guid TicketId { get; init; } = ticketId;

    public string Reference { get; init; } = reference;

    public Guid MessageId { get; init; } = messageId;

    public Guid CustomerId { get; init; } = customerId;

    public Guid AuthorId { get; init; } = authorId;

    public TicketAuthorKind AuthorKind { get; init; } = authorKind;

    public TicketMessageVisibility Visibility { get; init; } = visibility;

    public string Subject { get; init; } = subject;

    public string Body { get; init; } = body;

    public DateTime PostedOnUtc { get; init; } = postedOnUtc;
}

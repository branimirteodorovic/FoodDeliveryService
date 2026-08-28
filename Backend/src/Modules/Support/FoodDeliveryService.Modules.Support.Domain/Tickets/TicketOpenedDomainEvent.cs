using FoodDeliveryService.Common.Domain;

namespace FoodDeliveryService.Modules.Support.Domain.Tickets;

/// <summary>
/// Carries the whole opening snapshot, so the handler that turns it into
/// SupportTicketOpenedIntegrationEvent never reads the aggregate back (hard rule #9).
/// </summary>
public sealed class TicketOpenedDomainEvent(
    Guid ticketId,
    string reference,
    Guid customerId,
    Guid? orderId,
    string subject,
    TicketCategory category,
    TicketPriority priority,
    TicketSource source,
    DateTime openedOnUtc) : DomainEvent
{
    public Guid TicketId { get; init; } = ticketId;

    public string Reference { get; init; } = reference;

    public Guid CustomerId { get; init; } = customerId;

    public Guid? OrderId { get; init; } = orderId;

    public string Subject { get; init; } = subject;

    public TicketCategory Category { get; init; } = category;

    public TicketPriority Priority { get; init; } = priority;

    public TicketSource Source { get; init; } = source;

    public DateTime OpenedOnUtc { get; init; } = openedOnUtc;
}

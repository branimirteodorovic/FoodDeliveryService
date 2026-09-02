using FoodDeliveryService.Common.Domain;

namespace FoodDeliveryService.Modules.Support.Domain.Refunds;

/// <summary>
/// An agent asked for a customer to be refunded. Carries the amount and the reason so the
/// integration event built from it is a full snapshot without a second read of the aggregate.
/// </summary>
public sealed class RefundRequestedDomainEvent(
    Guid refundRequestId,
    Guid ticketId,
    string ticketReference,
    Guid orderId,
    Guid customerId,
    decimal amount,
    string reason,
    Guid requestedByAgentId,
    DateTime requestedOnUtc) : DomainEvent
{
    public Guid RefundRequestId { get; init; } = refundRequestId;

    public Guid TicketId { get; init; } = ticketId;

    /// <summary>The ticket's human-quotable reference — what the customer's email quotes.</summary>
    public string TicketReference { get; init; } = ticketReference;

    public Guid OrderId { get; init; } = orderId;

    public Guid CustomerId { get; init; } = customerId;

    public decimal Amount { get; init; } = amount;

    public string Reason { get; init; } = reason;

    public Guid RequestedByAgentId { get; init; } = requestedByAgentId;

    public DateTime RequestedOnUtc { get; init; } = requestedOnUtc;
}

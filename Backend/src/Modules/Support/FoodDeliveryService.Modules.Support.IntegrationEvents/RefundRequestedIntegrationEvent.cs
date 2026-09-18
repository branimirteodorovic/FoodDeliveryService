using FoodDeliveryService.Common.Application.EventBus;

namespace FoodDeliveryService.Modules.Support.IntegrationEvents;

/// <summary>
/// An agent has asked for a customer to be refunded, and an administrator has not yet decided.
/// <para>
/// <strong>Nothing consumes this one, and nothing should.</strong> The natural assumption on reading
/// a refund event is that a payment is being reversed somewhere — and since Feature 3.8 one is, but
/// off the <em>approved</em> event rather than this one. That is the point of there being two: a
/// request is a thing an agent asked for, and money must not move until a second person has agreed.
/// This event exists to make the queue visible outside Support — a dashboard, an alert on refunds
/// piling up — and a consumer that moved money on it would defeat the approval step entirely.
/// </para>
/// </summary>
public sealed class RefundRequestedIntegrationEvent : IntegrationEvent
{
    public RefundRequestedIntegrationEvent(
        Guid id,
        DateTime occurredOnUtc,
        Guid refundRequestId,
        Guid ticketId,
        string ticketReference,
        Guid orderId,
        Guid customerId,
        decimal amount,
        string reason,
        Guid requestedByAgentId,
        DateTime requestedOnUtc)
        : base(id, occurredOnUtc)
    {
        RefundRequestId = refundRequestId;
        TicketId = ticketId;
        TicketReference = ticketReference;
        OrderId = orderId;
        CustomerId = customerId;
        Amount = amount;
        Reason = reason;
        RequestedByAgentId = requestedByAgentId;
        RequestedOnUtc = requestedOnUtc;
    }

    public Guid RefundRequestId { get; init; }

    public Guid TicketId { get; init; }

    /// <summary>The ticket's human-quotable reference — what the customer's email quotes.</summary>
    public string TicketReference { get; init; }

    public Guid OrderId { get; init; }

    public Guid CustomerId { get; init; }

    public decimal Amount { get; init; }

    public string Reason { get; init; }

    public Guid RequestedByAgentId { get; init; }

    public DateTime RequestedOnUtc { get; init; }
}

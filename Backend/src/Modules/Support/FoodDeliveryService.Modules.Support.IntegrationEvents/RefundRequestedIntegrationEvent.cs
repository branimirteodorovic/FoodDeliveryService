using FoodDeliveryService.Common.Application.EventBus;

namespace FoodDeliveryService.Modules.Support.IntegrationEvents;

/// <summary>
/// An agent has asked for a customer to be refunded, and an administrator has not yet decided.
/// <para>
/// <strong>Nothing in Orders consumes this, and no money moves.</strong> The natural assumption on
/// reading a refund event is that a payment is being reversed somewhere; this platform has no
/// payment processing by design, so the event exists to make the request visible outside Support —
/// an approval queue, a dashboard, an alert on refunds piling up. A real payment integration would
/// consume the <em>approved</em> event, not this one.
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

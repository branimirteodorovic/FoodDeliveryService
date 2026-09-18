using FoodDeliveryService.Common.Application.EventBus;

namespace FoodDeliveryService.Modules.Payments.IntegrationEvents;

/// <summary>
/// An approved refund moved no money — Feature 3.8 Milestone H, §10.1.
/// <para>
/// The counterpart of <see cref="RefundSettledIntegrationEvent"/>, and the reason the refusals in
/// Payments are published rather than logged. An agent raised the request and an administrator
/// agreed to it; if the money then cannot move, the one outcome nobody can defend is the request
/// sitting at "approved" while the customer waits. Support transitions it to <c>Failed</c> and
/// writes an audit entry naming <see cref="Reason"/>, which is where a human reads it.
/// </para>
/// <para>
/// <b>Notifications deliberately does not consume this.</b> Three of the four reasons mean a person
/// has to do something — settle a cash order by hand, look at why a capture never happened, re-raise
/// a smaller request — and none of them are things the customer can act on. Telling them their
/// refund failed, with no next step and before an agent has even seen it, would be worse service
/// than the agent getting there first.
/// </para>
/// <para>
/// <see cref="Reason"/> is bounded to the <c>RefundFailureReason</c> constants —
/// <c>not_card_payment</c>, <c>payment_not_captured</c>, <c>amount_exceeds_captured</c>,
/// <c>gateway_error</c> — and is never Stripe's own message.
/// </para>
/// </summary>
public sealed class RefundFailedIntegrationEvent : IntegrationEvent
{
    public RefundFailedIntegrationEvent(
        Guid id,
        DateTime occurredOnUtc,
        Guid refundId,
        Guid refundRequestId,
        Guid ticketId,
        string ticketReference,
        Guid orderId,
        Guid customerId,
        decimal amount,
        string currency,
        string reason,
        DateTime failedOnUtc)
        : base(id, occurredOnUtc)
    {
        RefundId = refundId;
        RefundRequestId = refundRequestId;
        TicketId = ticketId;
        TicketReference = ticketReference;
        OrderId = orderId;
        CustomerId = customerId;
        Amount = amount;
        Currency = currency;
        Reason = reason;
        FailedOnUtc = failedOnUtc;
    }

    public Guid RefundId { get; init; }

    /// <summary>Support's own <c>RefundRequest.Id</c> — how the consumer finds the request this answers.</summary>
    public Guid RefundRequestId { get; init; }

    public Guid TicketId { get; init; }

    public string TicketReference { get; init; }

    public Guid OrderId { get; init; }

    public Guid CustomerId { get; init; }

    public decimal Amount { get; init; }

    /// <summary>ISO 4217, upper case.</summary>
    public string Currency { get; init; }

    /// <summary>One of the bounded reason codes — see the type remarks.</summary>
    public string Reason { get; init; }

    public DateTime FailedOnUtc { get; init; }
}

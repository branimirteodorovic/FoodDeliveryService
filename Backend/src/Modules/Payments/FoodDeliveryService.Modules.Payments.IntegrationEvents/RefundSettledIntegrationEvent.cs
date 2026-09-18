using FoodDeliveryService.Common.Application.EventBus;

namespace FoodDeliveryService.Modules.Payments.IntegrationEvents;

/// <summary>
/// The money is on its way back to the customer — Feature 3.8 Milestone H, §1.3 step 7.
/// <para>
/// <b>This is the event that makes Support's refunds real.</b> Support published
/// <c>RefundApproved</c> and, until this milestone, nothing anywhere acted on it; the platform's own
/// documentation said as much in eleven places (§10.3). This is the answer coming back: Support
/// moves its request to <c>Settled</c> and appends an audit entry, and Notifications emails the
/// customer that the refund has actually been sent rather than merely agreed.
/// </para>
/// <para>
/// It carries the ticket and its reference as well as the order, because both consumers need them
/// and hard rule #9 says they must not ask: Support matches the settlement to the request it
/// approved, and the customer's email quotes the reference.
/// </para>
/// <para>
/// <b>No <c>re_…</c>.</b> Same as its siblings — the provider's identifiers stay in this service.
/// </para>
/// </summary>
public sealed class RefundSettledIntegrationEvent : IntegrationEvent
{
    public RefundSettledIntegrationEvent(
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
        DateTime settledOnUtc)
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
        SettledOnUtc = settledOnUtc;
    }

    public Guid RefundId { get; init; }

    /// <summary>Support's own <c>RefundRequest.Id</c> — how the consumer finds the request this answers.</summary>
    public Guid RefundRequestId { get; init; }

    public Guid TicketId { get; init; }

    /// <summary>The ticket's human-quotable reference — what the customer's email quotes.</summary>
    public string TicketReference { get; init; }

    public Guid OrderId { get; init; }

    public Guid CustomerId { get; init; }

    /// <summary>The amount refunded, in major units — 12.99, not 1299.</summary>
    public decimal Amount { get; init; }

    /// <summary>ISO 4217, upper case.</summary>
    public string Currency { get; init; }

    public DateTime SettledOnUtc { get; init; }
}

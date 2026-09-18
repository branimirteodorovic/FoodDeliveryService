using FoodDeliveryService.Common.Domain;

namespace FoodDeliveryService.Modules.Payments.Domain.Refunds;

/// <summary>
/// An approved refund did not move any money — Feature 3.8 Milestone H, §10.1.
/// <para>
/// Raised for a provider refusal and for each of the three refusals this service makes without
/// calling anybody. It exists so that the refusal reaches the people who were told the refund was
/// agreed: Support transitions the request and writes an audit entry naming
/// <see cref="Reason"/>, which is what an agent sees. Nothing about it is retryable by a machine —
/// a cash order and an over-large amount both need a person.
/// </para>
/// </summary>
public sealed class RefundFailedDomainEvent(
    Guid refundId,
    Guid refundRequestId,
    Guid ticketId,
    string ticketReference,
    Guid orderId,
    Guid customerId,
    decimal amount,
    string currency,
    string reason,
    DateTime failedOnUtc) : DomainEvent
{
    public Guid RefundId { get; init; } = refundId;

    public Guid RefundRequestId { get; init; } = refundRequestId;

    public Guid TicketId { get; init; } = ticketId;

    public string TicketReference { get; init; } = ticketReference;

    public Guid OrderId { get; init; } = orderId;

    public Guid CustomerId { get; init; } = customerId;

    public decimal Amount { get; init; } = amount;

    public string Currency { get; init; } = currency;

    /// <summary>One of the bounded <see cref="RefundFailureReason"/> constants — never Stripe's own text.</summary>
    public string Reason { get; init; } = reason;

    public DateTime FailedOnUtc { get; init; } = failedOnUtc;
}

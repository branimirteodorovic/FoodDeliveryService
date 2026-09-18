using FoodDeliveryService.Common.Domain;

namespace FoodDeliveryService.Modules.Payments.Domain.Refunds;

/// <summary>
/// Money has gone back to the customer's card — Feature 3.8 Milestone H, §1.3 step 7. Raised when
/// the provider accepted the refund for an approved support request.
/// <para>
/// Carries the ticket and its reference as well as the order, because both downstream consumers
/// need them and neither may ask Support: Support itself matches the settlement to the request it
/// approved, and Notifications quotes the reference in the customer's email.
/// </para>
/// </summary>
public sealed class RefundSettledDomainEvent(
    Guid refundId,
    Guid refundRequestId,
    Guid ticketId,
    string ticketReference,
    Guid orderId,
    Guid customerId,
    decimal amount,
    string currency,
    DateTime settledOnUtc) : DomainEvent
{
    public Guid RefundId { get; init; } = refundId;

    public Guid RefundRequestId { get; init; } = refundRequestId;

    public Guid TicketId { get; init; } = ticketId;

    public string TicketReference { get; init; } = ticketReference;

    public Guid OrderId { get; init; } = orderId;

    public Guid CustomerId { get; init; } = customerId;

    public decimal Amount { get; init; } = amount;

    public string Currency { get; init; } = currency;

    public DateTime SettledOnUtc { get; init; } = settledOnUtc;
}

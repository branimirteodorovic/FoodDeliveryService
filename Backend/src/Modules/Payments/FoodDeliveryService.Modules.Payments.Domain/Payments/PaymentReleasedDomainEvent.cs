using FoodDeliveryService.Common.Domain;

namespace FoodDeliveryService.Modules.Payments.Domain.Payments;

/// <summary>
/// The hold is gone and the card was never charged — Feature 3.8 Milestone G, §9. Raised when the
/// restaurant rejected the order or the customer cancelled it.
/// <para>
/// <b>Not a refund.</b> Nothing was captured, so nothing is given back: the authorization is
/// cancelled at the provider and the customer's available balance recovers without a transaction
/// ever appearing on their statement. The refund path is §10 and starts from
/// <see cref="PaymentStatus.Captured"/>.
/// </para>
/// <para>
/// It does not say <em>why</em> the order ended. Orders already knows — it is the service that
/// rejected or cancelled it — and a reason invented here would be this service repeating, less
/// accurately, news that travelled on <c>OrderRejected</c>/<c>OrderCancelled</c> in the first place.
/// </para>
/// </summary>
public sealed class PaymentReleasedDomainEvent(
    Guid paymentId,
    Guid orderId,
    Guid customerId,
    decimal amount,
    string currency,
    DateTime releasedOnUtc) : DomainEvent
{
    public Guid PaymentId { get; init; } = paymentId;

    public Guid OrderId { get; init; } = orderId;

    public Guid CustomerId { get; init; } = customerId;

    public decimal Amount { get; init; } = amount;

    public string Currency { get; init; } = currency;

    public DateTime ReleasedOnUtc { get; init; } = releasedOnUtc;
}

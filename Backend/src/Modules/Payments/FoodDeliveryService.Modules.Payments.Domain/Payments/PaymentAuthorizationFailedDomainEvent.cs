using FoodDeliveryService.Common.Domain;

namespace FoodDeliveryService.Modules.Payments.Domain.Payments;

/// <summary>
/// The card was never charged and never will be for this order — Feature 3.8 Milestone F, §8.4.
/// <para>
/// <see cref="Reason"/> is one of the <see cref="PaymentFailureReason"/> constants and never Stripe's
/// own message. That bound is the reason this event can be counted (<c>payments.failed</c>, tagged by
/// reason) and emailed about at all: the provider's text is unbounded, customer-facing and written by
/// a third party.
/// </para>
/// </summary>
public sealed class PaymentAuthorizationFailedDomainEvent(
    Guid paymentId,
    Guid orderId,
    Guid customerId,
    string reason,
    DateTime failedOnUtc) : DomainEvent
{
    public Guid PaymentId { get; init; } = paymentId;

    public Guid OrderId { get; init; } = orderId;

    public Guid CustomerId { get; init; } = customerId;

    /// <summary>One of <see cref="PaymentFailureReason"/>. Bounded, and checked by the aggregate.</summary>
    public string Reason { get; init; } = reason;

    public DateTime FailedOnUtc { get; init; } = failedOnUtc;
}

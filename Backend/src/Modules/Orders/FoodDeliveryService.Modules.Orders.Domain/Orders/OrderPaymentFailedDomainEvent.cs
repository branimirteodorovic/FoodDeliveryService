using FoodDeliveryService.Common.Domain;

namespace FoodDeliveryService.Modules.Orders.Domain.Orders;

/// <summary>
/// An order was cancelled because its card was refused — Feature 3.8 Milestone F, §8.3.
/// <para>
/// <b>Distinct from <see cref="OrderCancelledDomainEvent"/> on purpose, and the reason is not
/// cosmetic.</b> Payments consumes <c>OrderCancelled</c> in order to <em>release an
/// authorization</em> (§9) — and reusing it here would ask Payments to release the hold of a payment
/// that just failed to be held. The terminal-state no-op in the aggregate absorbs that, but relying
/// on an absorption is not the same as not sending the message.
/// </para>
/// <para>
/// The second reason is the customer: "your payment was declined" and "you cancelled your order" are
/// different emails, and a consumer cannot tell them apart from one event.
/// </para>
/// </summary>
public sealed class OrderPaymentFailedDomainEvent(
    Guid orderId,
    Guid customerId,
    Guid restaurantId,
    OrderStatus previousStatus,
    string reason,
    DateTime failedOnUtc) : DomainEvent
{
    public Guid OrderId { get; init; } = orderId;

    public Guid CustomerId { get; init; } = customerId;

    public Guid RestaurantId { get; init; } = restaurantId;

    /// <summary>Where the order was before this cancelled it — the tag that makes the transition metric readable.</summary>
    public OrderStatus PreviousStatus { get; init; } = previousStatus;

    /// <summary>
    /// The bounded reason code as Payments published it. Carried rather than re-derived: Orders has
    /// no view of a card and no business inventing a description of why one was refused.
    /// </summary>
    public string Reason { get; init; } = reason;

    public DateTime FailedOnUtc { get; init; } = failedOnUtc;
}

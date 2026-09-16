namespace FoodDeliveryService.Modules.Orders.Domain.Orders;

/// <summary>
/// Where an order's money is — a <b>second, independent dimension</b> beside
/// <see cref="OrderStatus"/>, not an extension of it. Feature 3.8 Milestone F, §1.2 and §8.2.
/// <para>
/// The obvious modelling was a ninth <see cref="OrderStatus"/> member, <c>AwaitingPayment</c>, in
/// front of <c>Pending</c>. It was rejected deliberately. <see cref="OrderStatus"/> is consumed by
/// Delivery, projected into Support's order timeline, pushed as RealTime frames and asserted by four
/// integration-test suites; a ninth member ripples through every one of them for no behavioural
/// gain. A separate column has a blast radius of one module, and the two dimensions really are
/// orthogonal — a cash order is <see cref="NotRequired"/> from placement to delivery, whatever its
/// lifecycle does.
/// </para>
/// <para>
/// This is a <b>projection of the Payments service's state</b>, not a source of truth: it moves when
/// a payment integration event arrives. The only decision it drives locally is the guard on
/// <c>Order.Accept()</c>.
/// </para>
/// </summary>
public enum PaymentStatus
{
    /// <summary>A cash order. Nothing to authorize, nothing to capture, nothing to release.</summary>
    NotRequired = 1,

    /// <summary>
    /// A card order that has been placed and whose hold has not come back yet. Typically under a
    /// second — it is the window in which <c>Order.Accept()</c> refuses.
    /// </summary>
    Authorizing = 2,

    /// <summary>The funds are held on the card. The restaurant may accept.</summary>
    Authorized = 3,

    /// <summary>The money has been taken — projected from <c>PaymentCaptured</c> when §9 lands.</summary>
    Captured = 4,

    /// <summary>The hold was cancelled uncharged — projected from <c>PaymentReleased</c> in §9.</summary>
    Released = 5,

    /// <summary>
    /// The card was not charged and will not be. The order is <c>Cancelled</c> alongside it —
    /// <c>Order.FailPayment</c> sets both, and raises its own distinct domain event rather than
    /// reusing the customer's cancellation (§8.3).
    /// </summary>
    Failed = 6
}

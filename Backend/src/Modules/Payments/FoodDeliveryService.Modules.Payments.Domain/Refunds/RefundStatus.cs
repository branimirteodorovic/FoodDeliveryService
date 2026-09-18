namespace FoodDeliveryService.Modules.Payments.Domain.Refunds;

/// <summary>
/// Where one refund stands — Feature 3.8 Milestone H, §10.1.
/// <para>
/// Not to be confused with <c>Support.Domain.Refunds.RefundStatus</c>, which tracks a different
/// thing: that one is the state of the <em>request</em> a human made (requested, approved,
/// rejected), this one is the state of the money. Support's request reaches its own
/// <c>Settled</c>/<c>Failed</c> members by consuming what this enum decides, so the two are a chain
/// rather than a duplicate.
/// </para>
/// </summary>
public enum RefundStatus
{
    /// <summary>
    /// The row exists and the provider has not answered. Written <b>before</b> the Stripe call, for
    /// the same reason <see cref="Payments.PaymentStatus.Authorizing"/> is: a refund the provider
    /// performed and this platform has no row for is money that left the business with nothing
    /// pointing at it.
    /// </summary>
    Pending = 1,

    /// <summary>
    /// Stripe accepted the refund. Includes a refund it reports as <c>pending</c> — see
    /// <see cref="Refund.Settle"/> for why an accepted-but-not-yet-cleared refund is settled here
    /// rather than held in <see cref="Pending"/>.
    /// </summary>
    Settled = 2,

    /// <summary>
    /// No money went back, and none will without a new decision. Covers both a provider refusal and
    /// the guards that stop a refund before a call is made at all — a cash order, an uncaptured
    /// payment, an amount above what was taken.
    /// </summary>
    Failed = 3
}

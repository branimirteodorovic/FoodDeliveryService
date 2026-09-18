namespace FoodDeliveryService.Modules.Support.Domain.Refunds;

/// <summary>
/// Where a refund request stands. Explicit values: the number is persisted on every row, so a
/// member may be appended but never renumbered or reordered.
/// <para>
/// <b>This enum used to end at <see cref="Rejected"/>, and its absence of any settled state was
/// documented here as the design</b> — the platform processed no payments, so an approval was a
/// record of a decision and nothing moved behind it. Feature 3.8 (`PAYMENTS_PHASE3_PLAN.md` §10)
/// built the payment service that reverses it: an approval is now published, Payments refunds the
/// captured card payment against it, and the answer comes back as one of the two members below.
/// The approval trail itself is unchanged, and is still what authorizes any of it —
/// <see cref="RefundRequest"/> explains why that ordering is worth keeping.
/// </para>
/// </summary>
public enum RefundStatus
{
    /// <summary>An agent has asked for the refund; an administrator has not yet decided.</summary>
    Requested = 0,

    /// <summary>
    /// An administrator agreed. Money has not moved <em>yet</em>: Payments is acting on the
    /// approval asynchronously, and this is the state a request sits in until it answers.
    /// </summary>
    Approved = 1,

    Rejected = 2,

    /// <summary>
    /// The money is on its way back to the customer — Payments accepted the refund at the provider.
    /// Terminal.
    /// </summary>
    Settled = 3,

    /// <summary>
    /// The approval stands but no money moved, and none will without somebody doing something: the
    /// order was paid in cash, the card payment was never captured, the amount exceeded what was
    /// taken, or the provider refused. Terminal here — a fresh request is how a refund is tried
    /// again, so that the second attempt gets its own approval rather than silently inheriting the
    /// first one.
    /// </summary>
    Failed = 4
}

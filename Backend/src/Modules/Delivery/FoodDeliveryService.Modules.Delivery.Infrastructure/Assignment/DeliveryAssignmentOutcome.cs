namespace FoodDeliveryService.Modules.Delivery.Infrastructure.Assignment;

/// <summary>
/// How one turn of the offer cycle ended. It exists because <c>Result</c> cannot say: the routine
/// answers <c>Result.Success()</c> for "a driver was offered", for "nobody was in range, parked as
/// Unassigned" and for "somebody else already moved this delivery", and those three are the whole
/// point of the assignment panel. The values are the tag values of
/// <c>delivery.assignment.outcome</c>, so this enum is also the cardinality bound.
/// </summary>
internal enum DeliveryAssignmentOutcome
{
    /// <summary>A driver was found and the delivery was offered to them.</summary>
    Offered,

    /// <summary>
    /// The candidate search came back empty and the delivery was parked <c>Unassigned</c> — the
    /// outcome that waits on a human, and the one worth alerting on.
    /// </summary>
    NoDriver,

    /// <summary>
    /// The Caching 2.3 offer or driver lock was already held, so this trigger stood down. Its own
    /// value rather than a generic failure: a rising rate here is the early warning that the 5 s
    /// offer lock is too coarse, and it is invisible in every other signal.
    /// </summary>
    LockContended,

    /// <summary>
    /// The delivery had already left <c>Pending</c> — a redelivered event or a raced job tick
    /// re-entering the idempotent path. Benign, but it inflates "attempts" if folded into Offered.
    /// </summary>
    NotPending,

    /// <summary>An aggregate guard refused the offer or the unassign — a real error.</summary>
    Failed,

    /// <summary>
    /// An offer window lapsed with no answer from the driver. Recorded by
    /// <see cref="ProcessExpiredOffersJob"/> at detection, not by the routine — it is the outcome of
    /// the PREVIOUS offer, and the re-offer it triggers is counted separately on its own turn.
    /// </summary>
    Expired
}

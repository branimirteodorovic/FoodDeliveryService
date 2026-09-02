using FoodDeliveryService.Common.Application.Caching;

namespace FoodDeliveryService.Modules.Support.Application.Abstractions.Locking;

/// <summary>
/// The resources this module serializes on, built through the shared key convention so a read side
/// and a write side can never drift onto different names for the same lock — the reason the keys
/// live in one static rather than being composed at each call site.
/// </summary>
public static class SupportLocks
{
    /// <summary>
    /// How long an acquisition survives without an explicit release. Comfortably longer than the
    /// critical section it guards — one read and one database transaction — and far shorter than
    /// any business window, so a holder that crashes mid-claim leaves the ticket claimable again
    /// within seconds rather than parking it in the queue indefinitely.
    /// </summary>
    public static readonly TimeSpan ClaimTtl = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Held while one ticket's assignee is being decided. Claiming is check-then-act — read the
    /// ticket, see it unassigned, write the assignee — across two round trips, and no aggregate in
    /// this codebase carries a concurrency token, so nothing in the database would refuse the
    /// second write. Serializing on the ticket makes the loser observe the committed assignment and
    /// fail cleanly instead of overwriting it.
    /// </summary>
    public static string Ticket(Guid ticketId) => CacheKeys.Create("support", "ticket-lock", ticketId);

    /// <summary>
    /// How long a refund decision holds its lock. The same five seconds as a claim, for the same
    /// reason — one read plus one transaction — and deliberately not longer: an administrator who
    /// walks away mid-decision must not park the request, and the aggregate refuses the second
    /// decision anyway once the first has committed.
    /// </summary>
    public static readonly TimeSpan DecisionTtl = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Held while one refund request is being approved or rejected. The same check-then-act shape
    /// as a claim — read the status, see it undecided, write the decision — over two round trips
    /// with no concurrency token behind it, except that here the losing write would record a second
    /// administrator agreeing to money leaving the business.
    /// <para>
    /// Keyed on the refund request rather than on its ticket: approve and reject contend with each
    /// other and with themselves, and nothing about them contends with a claim on the same ticket.
    /// </para>
    /// </summary>
    public static string Refund(Guid refundRequestId) =>
        CacheKeys.Create("support", "refund-lock", refundRequestId);
}

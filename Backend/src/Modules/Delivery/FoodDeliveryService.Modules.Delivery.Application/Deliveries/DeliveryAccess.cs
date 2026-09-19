using FoodDeliveryService.Modules.Delivery.Application.Abstractions.Authentication;

namespace FoodDeliveryService.Modules.Delivery.Application.Deliveries;

/// <summary>
/// The one place that answers "may this caller see this delivery?": the order's customer, the
/// assigned driver, the driver currently holding a live offer on it, or an administrator —
/// recognized by the admin-only <see cref="Permissions.AdministerDeliveries"/> permission (the
/// ownership bypass).
///
/// It answers in SQL rather than in a branch after the read, and that is the whole point. A caller
/// who is none of the four gets no row, so there is no code path on which an existence-revealing
/// status could be returned instead of the 404 — the same shape Support's <c>TicketAccess</c> uses,
/// and the platform's convention: <b>404, not 403, when the resource is not the caller's</b>.
/// </summary>
internal static class DeliveryAccess
{
    /// <summary>
    /// The offered driver's window onto a delivery that is not yet theirs.
    /// <para>
    /// <c>OfferTo</c> sets <c>offered_driver_id</c> and leaves <c>driver_id</c> null until the
    /// driver accepts, so without this clause the one person who has to decide on the offer is
    /// precisely the one <see cref="VisibleToCallerSql"/> hides it from. It is bounded by the
    /// offer's own deadline rather than by the row being cleared: <c>ProcessExpiredOffersJob</c>
    /// nulls <c>offered_driver_id</c> on its own tick, so between the deadline and that tick the
    /// column still names a driver whose offer is already dead — and after a re-offer elsewhere it
    /// names somebody else entirely. Reading the deadline instead of the clear makes the window
    /// close exactly when the driver's authority to accept does.
    /// </para>
    /// </summary>
    internal const string LiveOfferSql =
        "(d.offered_driver_id = @UserId AND d.offer_expires_on_utc > @UtcNow)";

    /// <summary>
    /// The visibility predicate, to be ANDed onto a query over <c>deliveries d</c>. Its three
    /// parameters — <c>@IsAdmin</c>, <c>@UserId</c> and <c>@UtcNow</c> — come from
    /// <see cref="CanViewAnyDelivery"/>, <see cref="IDeliveryContext.UserId"/> and
    /// <c>IDateTimeProvider</c>. Kept as one constant so the two detail read paths cannot drift
    /// onto different definitions of "yours".
    /// </summary>
    internal const string VisibleToCallerSql =
        $"(@IsAdmin OR d.customer_id = @UserId OR d.driver_id = @UserId OR {LiveOfferSql})";

    internal static bool CanViewAnyDelivery(IDeliveryContext context) =>
        context.HasPermission(Permissions.AdministerDeliveries);
}

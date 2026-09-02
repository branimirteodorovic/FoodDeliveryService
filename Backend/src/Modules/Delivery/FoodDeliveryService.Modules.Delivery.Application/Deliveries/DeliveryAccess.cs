using FoodDeliveryService.Modules.Delivery.Application.Abstractions.Authentication;

namespace FoodDeliveryService.Modules.Delivery.Application.Deliveries;

/// <summary>
/// The one place that answers "may this caller see this delivery?": the order's customer, the
/// assigned driver, or an administrator — recognized by the admin-only
/// <see cref="Permissions.AdministerDeliveries"/> permission (the ownership bypass).
///
/// It answers in SQL rather than in a branch after the read, and that is the whole point. A caller
/// who is none of the three gets no row, so there is no code path on which an existence-revealing
/// status could be returned instead of the 404 — the same shape Support's <c>TicketAccess</c> uses,
/// and the platform's convention: <b>404, not 403, when the resource is not the caller's</b>.
/// </summary>
internal static class DeliveryAccess
{
    /// <summary>
    /// The visibility predicate, to be ANDed onto a query over <c>deliveries d</c>. Its two
    /// parameters — <c>@IsAdmin</c> and <c>@UserId</c> — come from
    /// <see cref="CanViewAnyDelivery"/> and <see cref="IDeliveryContext.UserId"/>. Kept as one
    /// constant so the two read paths cannot drift onto different definitions of "yours".
    /// </summary>
    internal const string VisibleToCallerSql =
        "(@IsAdmin OR d.customer_id = @UserId OR d.driver_id = @UserId)";

    internal static bool CanViewAnyDelivery(IDeliveryContext context) =>
        context.HasPermission(Permissions.AdministerDeliveries);
}

namespace FoodDeliveryService.Modules.Orders.Application.Abstractions.Authentication;

public interface IOrdersContext
{
    /// <summary>
    /// The authenticated caller's user id (the `sub` claim) — the customer id for placement and
    /// cancellation, the manager id for ownership checks on status transitions.
    /// </summary>
    Guid UserId { get; }

    /// <summary>
    /// True when the caller's resolved permission set contains the given code. Used for the
    /// administrator bypass on the per-restaurant ownership check (see <c>OrderOwnership</c>).
    /// </summary>
    bool HasPermission(string permissionCode);
}

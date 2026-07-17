namespace FoodDeliveryService.Modules.Delivery.Application.Abstractions.Authentication;

public interface IDeliveryContext
{
    /// <summary>
    /// The authenticated caller's user id (the `sub` claim) — the driver id for self-scoped
    /// profile operations, since a Driver is keyed by its UserId.
    /// </summary>
    Guid UserId { get; }

    /// <summary>
    /// True when the caller's resolved permission set contains the given code. Used for the
    /// administrator bypass on self-only checks (see <c>Permissions.AdministerDeliveries</c>).
    /// </summary>
    bool HasPermission(string permissionCode);
}

using FoodDeliveryService.Common.Domain;
using FoodDeliveryService.Modules.Orders.Application.Abstractions.Authentication;
using FoodDeliveryService.Modules.Orders.Domain.Orders;
using FoodDeliveryService.Modules.Orders.Domain.Restaurants;

namespace FoodDeliveryService.Modules.Orders.Application.Orders;

/// <summary>
/// Shared write-guard for the manager-facing status transitions: only the manager who owns the
/// order's restaurant may accept/reject/advance it. Administrators bypass the check — recognized by
/// holding the admin-only <see cref="Permissions.Administer"/> permission (managers never hold it).
/// Mirrors Restaurants' <c>RestaurantOwnership</c>.
/// <para>
/// Feature 3.7 Milestone F. A non-owner gets <see cref="OrderErrors.NotFound"/> — the identical
/// response a nonexistent order gets — rather than the <c>Orders.NotOwner</c> 400 this used to
/// return. That 400 confirmed the id was real, which is the same enumeration oracle Milestone A
/// closed on the read paths (docs/security.md §2.2) and deferred here for the writes: it touches
/// four handlers through this one guard.
/// </para>
/// </summary>
internal static class OrderOwnership
{
    internal static Result EnsureCanManage(Restaurant restaurant, Guid orderId, IOrdersContext context)
    {
        if (restaurant.ManagerUserId == context.UserId || context.HasPermission(Permissions.Administer))
        {
            return Result.Success();
        }

        return Result.Failure(OrderErrors.NotFound(orderId));
    }
}

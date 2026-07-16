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
/// </summary>
internal static class OrderOwnership
{
    internal static Result EnsureCanManage(Restaurant restaurant, IOrdersContext context)
    {
        if (restaurant.ManagerUserId == context.UserId || context.HasPermission(Permissions.Administer))
        {
            return Result.Success();
        }

        return Result.Failure(OrderErrors.NotOwner);
    }
}

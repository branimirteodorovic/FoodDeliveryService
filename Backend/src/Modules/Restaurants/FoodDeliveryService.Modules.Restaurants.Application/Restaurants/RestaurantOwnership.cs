using FoodDeliveryService.Common.Domain;
using FoodDeliveryService.Modules.Restaurants.Application.Abstractions.Authentication;
using FoodDeliveryService.Modules.Restaurants.Domain.Restaurants;

namespace FoodDeliveryService.Modules.Restaurants.Application.Restaurants;

/// <summary>
/// Shared write-guard: only the owning manager may modify a restaurant or its menu.
/// Administrators bypass the check — recognized by holding the admin-only
/// <see cref="Permissions.CreateRestaurant"/> permission (managers are never granted it).
/// </summary>
internal static class RestaurantOwnership
{
    internal static Result EnsureCanModify(Restaurant restaurant, IRestaurantsContext context)
    {
        if (restaurant.ManagerUserId == context.UserId || context.HasPermission(Permissions.CreateRestaurant))
        {
            return Result.Success();
        }

        return Result.Failure(RestaurantErrors.NotManager);
    }
}

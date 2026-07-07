namespace FoodDeliveryService.Modules.Restaurants.Application.Abstractions.Authentication;

public interface IRestaurantsContext
{
    // The current user's module-side id (the Users service UserId placed in the sub claim by
    // CustomClaimsTransformation) — compared against Restaurant.ManagerUserId for ownership checks.
    Guid UserId { get; }

    // True when the current user's resolved permission set contains the given code. Used for the
    // administrator bypass on ownership checks.
    bool HasPermission(string permissionCode);
}

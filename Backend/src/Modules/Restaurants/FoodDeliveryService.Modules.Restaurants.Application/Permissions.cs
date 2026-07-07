namespace FoodDeliveryService.Modules.Restaurants.Application;

/// <summary>
/// Permission codes used by this module's endpoints. They must match the codes seeded in the Users
/// service (Users.Domain Permission + PermissionConfiguration) — permissions are resolved at
/// request time via GetUserPermissionsRequest and enforced by the permission policy provider.
/// </summary>
public static class Permissions
{
    public const string GetRestaurants = "restaurants:read";

    // Onboard restaurant + provision its manager — assigned to Administrator only, which is why
    // it doubles as the "is administrator" marker for the ownership-check bypass.
    public const string CreateRestaurant = "restaurants:create";

    public const string ModifyRestaurant = "restaurants:update";

    public const string ManageMenu = "menu:manage";

    public const string GetMenu = "menu:read";
}

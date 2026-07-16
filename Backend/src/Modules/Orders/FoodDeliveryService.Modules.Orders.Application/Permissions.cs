namespace FoodDeliveryService.Modules.Orders.Application;

// Permission codes mirror the rows seeded by the Users module (Permission.cs +
// PermissionConfiguration); authorization resolves them via GetUserPermissionsRequest.
public static class Permissions
{
    public const string CreateOrder = "orders:create";

    public const string GetOrders = "orders:read";

    // Manager-facing accept/reject/advance transitions — assigned to RestaurantManager and
    // Administrator only.
    public const string ManageOrders = "orders:manage";

    // Admin marker for the ownership-check bypass on manager transitions: Administrators are the
    // only role granted restaurants:create (restaurant onboarding), so it doubles as the "is
    // administrator" test — the same convention Restaurants' RestaurantOwnership uses.
    public const string Administer = "restaurants:create";
}

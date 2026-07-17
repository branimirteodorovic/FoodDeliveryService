namespace FoodDeliveryService.Modules.Delivery.Application;

/// <summary>
/// Permission codes used by this module's endpoints. They must match the codes seeded in the Users
/// service (Users.Domain Permission + PermissionConfiguration) — permissions are resolved at
/// request time via GetUserPermissionsRequest and enforced by the permission policy provider.
/// </summary>
public static class Permissions
{
    public const string GetDrivers = "drivers:read";

    // Own profile, vehicle, availability, location.
    public const string ModifyDriver = "drivers:update";

    public const string GetDeliveries = "deliveries:read";

    // Accept/reject an offer, picked-up, delivered (own deliveries).
    public const string ManageDeliveries = "deliveries:manage";

    // Admin-only: view/reassign any delivery. Doubles as the "is administrator" marker for
    // ownership-check bypasses (mirroring Restaurants' use of restaurants:create).
    public const string AdministerDeliveries = "deliveries:administer";

    // Onboard a driver (provision their invited account in Users) — Administrator only.
    public const string ProvisionUsers = "users:provision";
}

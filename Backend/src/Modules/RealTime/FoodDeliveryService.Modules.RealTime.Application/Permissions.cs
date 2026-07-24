namespace FoodDeliveryService.Modules.RealTime.Application;

// Permission codes mirror the rows seeded by the Users module (Permission.cs +
// PermissionConfiguration); authorization resolves them via GetUserPermissionsRequest. RealTime
// enforces no per-endpoint policy of its own (the hub is [Authorize] only) — these are used purely
// as identity markers so TrackingHub.OnConnectedAsync knows which dashboard groups to join.
public static class Permissions
{
    // Restaurants' own "manage my restaurant" permission — granted to RestaurantManager and
    // Administrator only (see Users' PermissionConfiguration). Reused here rather than minting a
    // dedicated permission, the same convention Orders' Permissions.Administer uses for
    // restaurants:create. Paired with a RestaurantManager replica row (Milestone D) to resolve the
    // specific restaurant; a caller with this permission but no replica row (e.g. Administrator)
    // simply joins no restaurant group.
    public const string RestaurantDashboard = "restaurants:update";

    // Support-dashboard audience (Milestone D) — granted only to the SupportAgent role.
    public const string SupportDashboard = "support:dashboard";
}

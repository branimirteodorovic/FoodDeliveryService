namespace FoodDeliveryService.Modules.Users.Domain.Users;

public sealed class Permission
{
    public static readonly Permission GetUser = new("users:read");
    public static readonly Permission ModifyUser = new("users:update");
    public static readonly Permission GetCart = new("carts:read");
    public static readonly Permission AddToCart = new("carts:add");
    public static readonly Permission RemoveFromCart = new("carts:remove");
    public static readonly Permission GetOrders = new("orders:read");
    public static readonly Permission CreateOrder = new("orders:create");
    public static readonly Permission ManageOrders = new("orders:manage"); // accept/reject/advance order status

    // Restaurants / menu (Phase 1). Assigned to roles in PermissionConfiguration.
    public static readonly Permission GetRestaurants = new("restaurants:read");
    public static readonly Permission CreateRestaurant = new("restaurants:create");   // = onboard restaurant + manager
    public static readonly Permission ModifyRestaurant = new("restaurants:update");
    public static readonly Permission ManageMenu = new("menu:manage");
    public static readonly Permission GetMenu = new("menu:read");
    public static readonly Permission ProvisionUsers = new("users:provision");        // create staff/partner accounts

    // Delivery / driver management (Phase 2, Feature 2.1). Assigned to roles in PermissionConfiguration.
    public static readonly Permission GetDrivers = new("drivers:read");
    public static readonly Permission ModifyDriver = new("drivers:update");            // own profile, vehicle, availability, location
    public static readonly Permission GetDeliveries = new("deliveries:read");
    public static readonly Permission ManageDeliveries = new("deliveries:manage");     // accept/reject an offer, picked-up, delivered (own)
    public static readonly Permission AdministerDeliveries = new("deliveries:administer"); // admin-only: view/reassign any delivery — the ownership bypass

    // Real-Time dashboards (Phase 2, Feature 2.2 Milestone D). RestaurantManager's dashboard group
    // reuses ModifyRestaurant (already unique to RestaurantManager + Administrator) rather than a
    // dedicated permission — see RealTime.Application.Permissions. SupportAgent has no comparable
    // existing signal, so it gets its own marker permission.
    public static readonly Permission ViewSupportDashboard = new("support:dashboard");

    public Permission(string code)
    {
        Code = code;
    }

    public string Code { get; }
}

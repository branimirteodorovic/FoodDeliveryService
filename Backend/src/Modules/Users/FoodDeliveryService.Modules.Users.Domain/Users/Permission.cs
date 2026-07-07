namespace FoodDeliveryService.Modules.Users.Domain.Users;

public sealed class Permission
{
    public static readonly Permission GetUser = new("users:read");
    public static readonly Permission ModifyUser = new("users:update");
    public static readonly Permission GetEvents = new("events:read");
    public static readonly Permission SearchEvents = new("events:search");
    public static readonly Permission ModifyEvents = new("events:update");
    public static readonly Permission GetTicketTypes = new("ticket-types:read");
    public static readonly Permission ModifyTicketTypes = new("ticket-types:update");
    public static readonly Permission GetCategories = new("categories:read");
    public static readonly Permission ModifyCategories = new("categories:update");
    public static readonly Permission GetCart = new("carts:read");
    public static readonly Permission AddToCart = new("carts:add");
    public static readonly Permission RemoveFromCart = new("carts:remove");
    public static readonly Permission GetOrders = new("orders:read");
    public static readonly Permission CreateOrder = new("orders:create");
    public static readonly Permission ManageOrders = new("orders:manage"); // accept/reject/advance order status
    public static readonly Permission GetTickets = new("tickets:read");
    public static readonly Permission CheckInTicket = new("tickets:check-in");
    public static readonly Permission GetEventStatistics = new("event-statistics:read");

    // Restaurants / menu (Phase 1). Assigned to roles in PermissionConfiguration.
    public static readonly Permission GetRestaurants = new("restaurants:read");
    public static readonly Permission CreateRestaurant = new("restaurants:create");   // = onboard restaurant + manager
    public static readonly Permission ModifyRestaurant = new("restaurants:update");
    public static readonly Permission ManageMenu = new("menu:manage");
    public static readonly Permission GetMenu = new("menu:read");
    public static readonly Permission ProvisionUsers = new("users:provision");        // create staff/partner accounts

    public Permission(string code)
    {
        Code = code;
    }

    public string Code { get; }
}

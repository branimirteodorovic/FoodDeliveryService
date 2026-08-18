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

    // Support & ticketing (Phase 3, Feature 3.6). NB: the existing `tickets:read` / `tickets:check-in`
    // above are event-ticketing leftovers from the Evently heritage and are granted to every Customer —
    // they are NOT these. Support codes are namespaced `support-*` for exactly that reason.
    public static readonly Permission OpenSupportTicket = new("support-tickets:open");        // customer: open a ticket, reply on their own
    public static readonly Permission GetSupportTickets = new("support-tickets:read");        // agent: read any; customer: read their own (ownership-scoped in the handler)
    public static readonly Permission ManageSupportTickets = new("support-tickets:manage");   // agent: status transitions, internal notes, audit log
    public static readonly Permission AssignSupportTickets = new("support-tickets:assign");   // agent: claim; admin: assign to anyone
    public static readonly Permission RequestRefund = new("refunds:request");                 // agent
    public static readonly Permission ApproveRefund = new("refunds:approve");                 // admin only — segregation of duties
    public static readonly Permission GetSupportAnalytics = new("support-analytics:read");

    public Permission(string code)
    {
        Code = code;
    }

    public string Code { get; }
}

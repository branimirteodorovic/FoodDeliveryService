using FoodDeliveryService.Modules.Users.Domain.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FoodDeliveryService.Modules.Users.Infrastructure.Users;

internal sealed class PermissionConfiguration : IEntityTypeConfiguration<Permission>
{
    public void Configure(EntityTypeBuilder<Permission> builder)
    {
        builder.ToTable("permissions");

        builder.HasKey(p => p.Code);

        builder.Property(p => p.Code).HasMaxLength(100);

        builder.HasData(
            Permission.GetUser,
            Permission.ModifyUser,
            Permission.GetCart,
            Permission.AddToCart,
            Permission.RemoveFromCart,
            Permission.GetOrders,
            Permission.CreateOrder,
            Permission.ManageOrders,
            Permission.GetRestaurants,
            Permission.CreateRestaurant,
            Permission.ModifyRestaurant,
            Permission.ManageMenu,
            Permission.GetMenu,
            Permission.ProvisionUsers,
            Permission.GetDrivers,
            Permission.ModifyDriver,
            Permission.GetDeliveries,
            Permission.ManageDeliveries,
            Permission.AdministerDeliveries,
            Permission.ViewSupportDashboard,
            Permission.OpenSupportTicket,
            Permission.GetSupportTickets,
            Permission.ManageSupportTickets,
            Permission.AssignSupportTickets,
            Permission.RequestRefund,
            Permission.ApproveRefund,
            Permission.GetSupportAnalytics);

        builder
            .HasMany<Role>()
            .WithMany()
            .UsingEntity(joinBuilder =>
            {
                joinBuilder.ToTable("role_permissions");

                joinBuilder.HasData(
                    // Customer permissions (was Member) — the only self-registering actor.
                    CreateRolePermission(Role.Customer, Permission.GetUser),
                    CreateRolePermission(Role.Customer, Permission.ModifyUser),
                    CreateRolePermission(Role.Customer, Permission.GetCart),
                    CreateRolePermission(Role.Customer, Permission.AddToCart),
                    CreateRolePermission(Role.Customer, Permission.RemoveFromCart),
                    CreateRolePermission(Role.Customer, Permission.GetOrders),
                    CreateRolePermission(Role.Customer, Permission.CreateOrder),
                    // Read-only restaurant browsing (full browse endpoints arrive with the ordering work).
                    CreateRolePermission(Role.Customer, Permission.GetRestaurants),
                    CreateRolePermission(Role.Customer, Permission.GetMenu),
                    // Track their own order's delivery. Ownership-scoped in the handler: a customer may
                    // only read a delivery for an order they placed.
                    CreateRolePermission(Role.Customer, Permission.GetDeliveries),
                    // Support: open a ticket and read their own. Ownership is enforced in the Support
                    // handlers — a customer reading someone else's ticket gets a 404, not a 403.
                    CreateRolePermission(Role.Customer, Permission.OpenSupportTicket),
                    CreateRolePermission(Role.Customer, Permission.GetSupportTickets),
                    // Admin permissions
                    CreateRolePermission(Role.Administrator, Permission.GetUser),
                    CreateRolePermission(Role.Administrator, Permission.ModifyUser),
                    CreateRolePermission(Role.Administrator, Permission.GetCart),
                    CreateRolePermission(Role.Administrator, Permission.AddToCart),
                    CreateRolePermission(Role.Administrator, Permission.RemoveFromCart),
                    CreateRolePermission(Role.Administrator, Permission.GetOrders),
                    CreateRolePermission(Role.Administrator, Permission.CreateOrder),
                    CreateRolePermission(Role.Administrator, Permission.ManageOrders),
                    // Restaurant oversight: admin can onboard/provision and manage any restaurant/menu.
                    CreateRolePermission(Role.Administrator, Permission.ProvisionUsers),
                    CreateRolePermission(Role.Administrator, Permission.CreateRestaurant),
                    CreateRolePermission(Role.Administrator, Permission.GetRestaurants),
                    CreateRolePermission(Role.Administrator, Permission.ModifyRestaurant),
                    CreateRolePermission(Role.Administrator, Permission.GetMenu),
                    CreateRolePermission(Role.Administrator, Permission.ManageMenu),
                    // Delivery oversight: admin can onboard drivers (ProvisionUsers, above) and view/
                    // reassign any delivery. AdministerDeliveries is the ownership bypass, mirroring how
                    // the Orders handlers let an admin act on any restaurant's order.
                    CreateRolePermission(Role.Administrator, Permission.GetDrivers),
                    CreateRolePermission(Role.Administrator, Permission.ModifyDriver),
                    CreateRolePermission(Role.Administrator, Permission.GetDeliveries),
                    CreateRolePermission(Role.Administrator, Permission.ManageDeliveries),
                    CreateRolePermission(Role.Administrator, Permission.AdministerDeliveries),
                    // Support oversight: everything an agent can do, plus refunds:approve — the
                    // segregation-of-duties permission an agent must never hold, since the agent who
                    // requests a refund cannot be the one who approves it.
                    CreateRolePermission(Role.Administrator, Permission.OpenSupportTicket),
                    CreateRolePermission(Role.Administrator, Permission.GetSupportTickets),
                    CreateRolePermission(Role.Administrator, Permission.ManageSupportTickets),
                    CreateRolePermission(Role.Administrator, Permission.AssignSupportTickets),
                    CreateRolePermission(Role.Administrator, Permission.RequestRefund),
                    CreateRolePermission(Role.Administrator, Permission.ApproveRefund),
                    CreateRolePermission(Role.Administrator, Permission.GetSupportAnalytics),
                    // RestaurantManager: manage only their own restaurant/menu (ownership-enforced in handlers)
                    // + their own profile. No CreateRestaurant/ProvisionUsers. Seeded now, exercised in later milestones.
                    CreateRolePermission(Role.RestaurantManager, Permission.GetRestaurants),
                    CreateRolePermission(Role.RestaurantManager, Permission.ModifyRestaurant),
                    CreateRolePermission(Role.RestaurantManager, Permission.ManageMenu),
                    CreateRolePermission(Role.RestaurantManager, Permission.GetMenu),
                    CreateRolePermission(Role.RestaurantManager, Permission.GetUser),
                    CreateRolePermission(Role.RestaurantManager, Permission.ModifyUser),
                    // Read + manage order status for their own restaurants (ownership enforced in the
                    // Orders handlers): orders:read powers the manager's "incoming orders" list.
                    CreateRolePermission(Role.RestaurantManager, Permission.GetOrders),
                    CreateRolePermission(Role.RestaurantManager, Permission.ManageOrders),
                    // DeliveryDriver: manage their own driver profile/availability/location and act on
                    // their own deliveries (ownership enforced in the Delivery handlers). No
                    // AdministerDeliveries — that ownership bypass is admin-only. Seeded now, exercised
                    // in later Delivery milestones.
                    CreateRolePermission(Role.DeliveryDriver, Permission.GetDrivers),
                    CreateRolePermission(Role.DeliveryDriver, Permission.ModifyDriver),
                    CreateRolePermission(Role.DeliveryDriver, Permission.GetDeliveries),
                    CreateRolePermission(Role.DeliveryDriver, Permission.ManageDeliveries),
                    CreateRolePermission(Role.DeliveryDriver, Permission.GetUser),
                    CreateRolePermission(Role.DeliveryDriver, Permission.ModifyUser),
                    // SupportAgent: the RealTime support dashboard's live global activity feed (Milestone
                    // D), plus their own profile, plus the operational ticketing set (Feature 3.6): read
                    // any ticket, drive its status, claim/assign it, request a refund and read the
                    // analytics summary. Deliberately NOT refunds:approve — that is admin-only, so the
                    // agent who requests a refund can never approve their own request. Also deliberately
                    // NOT support-tickets:open, which is the customer-facing "open a ticket" code.
                    CreateRolePermission(Role.SupportAgent, Permission.ViewSupportDashboard),
                    CreateRolePermission(Role.SupportAgent, Permission.GetUser),
                    CreateRolePermission(Role.SupportAgent, Permission.ModifyUser),
                    CreateRolePermission(Role.SupportAgent, Permission.GetSupportTickets),
                    CreateRolePermission(Role.SupportAgent, Permission.ManageSupportTickets),
                    CreateRolePermission(Role.SupportAgent, Permission.AssignSupportTickets),
                    CreateRolePermission(Role.SupportAgent, Permission.RequestRefund),
                    CreateRolePermission(Role.SupportAgent, Permission.GetSupportAnalytics));
            });
    }

    private static object CreateRolePermission(Role role, Permission permission)
    {
        return new
        {
            RoleName = role.Name,
            PermissionCode = permission.Code
        };
    }
}

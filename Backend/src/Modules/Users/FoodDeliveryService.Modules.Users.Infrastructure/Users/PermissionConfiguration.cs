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
            Permission.GetEvents,
            Permission.SearchEvents,
            Permission.ModifyEvents,
            Permission.GetTicketTypes,
            Permission.ModifyTicketTypes,
            Permission.GetCategories,
            Permission.ModifyCategories,
            Permission.GetCart,
            Permission.AddToCart,
            Permission.RemoveFromCart,
            Permission.GetOrders,
            Permission.CreateOrder,
            Permission.ManageOrders,
            Permission.GetTickets,
            Permission.CheckInTicket,
            Permission.GetEventStatistics,
            Permission.GetRestaurants,
            Permission.CreateRestaurant,
            Permission.ModifyRestaurant,
            Permission.ManageMenu,
            Permission.GetMenu,
            Permission.ProvisionUsers);

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
                    CreateRolePermission(Role.Customer, Permission.SearchEvents),
                    CreateRolePermission(Role.Customer, Permission.GetTicketTypes),
                    CreateRolePermission(Role.Customer, Permission.GetCart),
                    CreateRolePermission(Role.Customer, Permission.AddToCart),
                    CreateRolePermission(Role.Customer, Permission.RemoveFromCart),
                    CreateRolePermission(Role.Customer, Permission.GetOrders),
                    CreateRolePermission(Role.Customer, Permission.CreateOrder),
                    CreateRolePermission(Role.Customer, Permission.GetTickets),
                    CreateRolePermission(Role.Customer, Permission.CheckInTicket),
                    // Read-only restaurant browsing (full browse endpoints arrive with the ordering work).
                    CreateRolePermission(Role.Customer, Permission.GetRestaurants),
                    CreateRolePermission(Role.Customer, Permission.GetMenu),
                    // Admin permissions
                    CreateRolePermission(Role.Administrator, Permission.GetUser),
                    CreateRolePermission(Role.Administrator, Permission.ModifyUser),
                    CreateRolePermission(Role.Administrator, Permission.GetEvents),
                    CreateRolePermission(Role.Administrator, Permission.SearchEvents),
                    CreateRolePermission(Role.Administrator, Permission.ModifyEvents),
                    CreateRolePermission(Role.Administrator, Permission.GetTicketTypes),
                    CreateRolePermission(Role.Administrator, Permission.ModifyTicketTypes),
                    CreateRolePermission(Role.Administrator, Permission.GetCategories),
                    CreateRolePermission(Role.Administrator, Permission.ModifyCategories),
                    CreateRolePermission(Role.Administrator, Permission.GetCart),
                    CreateRolePermission(Role.Administrator, Permission.AddToCart),
                    CreateRolePermission(Role.Administrator, Permission.RemoveFromCart),
                    CreateRolePermission(Role.Administrator, Permission.GetOrders),
                    CreateRolePermission(Role.Administrator, Permission.CreateOrder),
                    CreateRolePermission(Role.Administrator, Permission.ManageOrders),
                    CreateRolePermission(Role.Administrator, Permission.GetTickets),
                    CreateRolePermission(Role.Administrator, Permission.CheckInTicket),
                    CreateRolePermission(Role.Administrator, Permission.GetEventStatistics),
                    // Restaurant oversight: admin can onboard/provision and manage any restaurant/menu.
                    CreateRolePermission(Role.Administrator, Permission.ProvisionUsers),
                    CreateRolePermission(Role.Administrator, Permission.CreateRestaurant),
                    CreateRolePermission(Role.Administrator, Permission.GetRestaurants),
                    CreateRolePermission(Role.Administrator, Permission.ModifyRestaurant),
                    CreateRolePermission(Role.Administrator, Permission.GetMenu),
                    CreateRolePermission(Role.Administrator, Permission.ManageMenu),
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
                    CreateRolePermission(Role.RestaurantManager, Permission.ManageOrders));
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

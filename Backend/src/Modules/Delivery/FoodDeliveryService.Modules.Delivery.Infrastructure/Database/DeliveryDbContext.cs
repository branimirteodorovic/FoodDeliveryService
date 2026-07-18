using FoodDeliveryService.Common.Infrastructure.Inbox;
using FoodDeliveryService.Common.Infrastructure.Outbox;
using FoodDeliveryService.Modules.Delivery.Application.Abstractions.Data;
using FoodDeliveryService.Modules.Delivery.Domain.Drivers;
using FoodDeliveryService.Modules.Delivery.Domain.Orders;
using FoodDeliveryService.Modules.Delivery.Domain.Restaurants;
using Microsoft.EntityFrameworkCore;

namespace FoodDeliveryService.Modules.Delivery.Infrastructure.Database;

public sealed class DeliveryDbContext(DbContextOptions<DeliveryDbContext> options)
    : DbContext(options), IUnitOfWork
{
    internal DbSet<Driver> Drivers { get; set; }

    internal DbSet<DriverLocationHistoryEntry> DriverLocationHistory { get; set; }

    // Local replicas of state owned by other services (Restaurants / Orders), kept current from
    // their integration events. Read-only projections — never mutated by Delivery's own commands.
    internal DbSet<Restaurant> Restaurants { get; set; }

    internal DbSet<Order> Orders { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Outbox/inbox tables live in Common.Infrastructure, so the assembly scan below does not
        // find them — they stay explicitly applied.
        modelBuilder.ApplyConfiguration(new OutboxMessageConfiguration());
        modelBuilder.ApplyConfiguration(new OutboxMessageConsumerConfiguration());
        modelBuilder.ApplyConfiguration(new InboxMessageConfiguration());
        modelBuilder.ApplyConfiguration(new InboxMessageConsumerConfiguration());

        modelBuilder.ApplyConfigurationsFromAssembly(typeof(DeliveryDbContext).Assembly);
    }
}

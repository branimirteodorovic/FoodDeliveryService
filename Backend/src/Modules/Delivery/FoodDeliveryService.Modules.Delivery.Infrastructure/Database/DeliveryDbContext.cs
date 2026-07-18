using FoodDeliveryService.Common.Infrastructure.Inbox;
using FoodDeliveryService.Common.Infrastructure.Outbox;
using FoodDeliveryService.Modules.Delivery.Application.Abstractions.Data;
using FoodDeliveryService.Modules.Delivery.Domain.Drivers;
using Microsoft.EntityFrameworkCore;

namespace FoodDeliveryService.Modules.Delivery.Infrastructure.Database;

public sealed class DeliveryDbContext(DbContextOptions<DeliveryDbContext> options)
    : DbContext(options), IUnitOfWork
{
    internal DbSet<Driver> Drivers { get; set; }

    internal DbSet<DriverLocationHistoryEntry> DriverLocationHistory { get; set; }

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

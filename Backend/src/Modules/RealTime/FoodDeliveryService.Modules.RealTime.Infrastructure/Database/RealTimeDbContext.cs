using FoodDeliveryService.Common.Infrastructure.Inbox;
using FoodDeliveryService.Modules.RealTime.Application.Abstractions.Data;
using FoodDeliveryService.Modules.RealTime.Infrastructure.Database.RestaurantManagers;
using Microsoft.EntityFrameworkCore;

namespace FoodDeliveryService.Modules.RealTime.Infrastructure.Database;

/// <summary>
/// The Real-Time service's first database (Milestone D). Deliberately narrow: one replica table
/// (<see cref="RestaurantManagers"/>) plus the inbox tables used to consume Restaurants' events
/// durably. No outbox — this service raises no domain events and publishes no integration events, so
/// <c>InsertOutboxMessagesInterceptor</c> is not registered on this context (contrast every other
/// module's DbContext).
/// </summary>
public sealed class RealTimeDbContext(DbContextOptions<RealTimeDbContext> options)
    : DbContext(options), IUnitOfWork
{
    internal DbSet<RestaurantManager> RestaurantManagers { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Inbox tables live in Common.Infrastructure, so the assembly scan below does not find them.
        modelBuilder.ApplyConfiguration(new InboxMessageConfiguration());
        modelBuilder.ApplyConfiguration(new InboxMessageConsumerConfiguration());

        modelBuilder.ApplyConfigurationsFromAssembly(typeof(RealTimeDbContext).Assembly);
    }
}

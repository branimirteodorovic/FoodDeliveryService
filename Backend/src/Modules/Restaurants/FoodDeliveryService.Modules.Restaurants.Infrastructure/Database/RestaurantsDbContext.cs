using FoodDeliveryService.Common.Infrastructure.Inbox;
using FoodDeliveryService.Common.Infrastructure.Outbox;
using FoodDeliveryService.Modules.Restaurants.Application.Abstractions.Data;
using FoodDeliveryService.Modules.Restaurants.Domain.Managers;
using FoodDeliveryService.Modules.Restaurants.Domain.Restaurants;
using Microsoft.EntityFrameworkCore;

namespace FoodDeliveryService.Modules.Restaurants.Infrastructure.Database;

public sealed class RestaurantsDbContext(DbContextOptions<RestaurantsDbContext> options)
    : DbContext(options), IUnitOfWork
{
    internal DbSet<Restaurant> Restaurants { get; set; }

    internal DbSet<RestaurantManager> RestaurantManagers { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Outbox/inbox tables live in Common.Infrastructure, so the assembly scan below does not
        // find them — they stay explicitly applied.
        modelBuilder.ApplyConfiguration(new OutboxMessageConfiguration());
        modelBuilder.ApplyConfiguration(new OutboxMessageConsumerConfiguration());
        modelBuilder.ApplyConfiguration(new InboxMessageConfiguration());
        modelBuilder.ApplyConfiguration(new InboxMessageConsumerConfiguration());

        modelBuilder.ApplyConfigurationsFromAssembly(typeof(RestaurantsDbContext).Assembly);
    }
}

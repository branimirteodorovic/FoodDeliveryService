using FoodDeliveryService.Common.Infrastructure.Inbox;
using FoodDeliveryService.Common.Infrastructure.Outbox;
using FoodDeliveryService.Modules.Orders.Application.Abstractions.Data;
using FoodDeliveryService.Modules.Orders.Domain.Customers;
using FoodDeliveryService.Modules.Orders.Domain.Orders;
using FoodDeliveryService.Modules.Orders.Domain.Restaurants;
using Microsoft.EntityFrameworkCore;

namespace FoodDeliveryService.Modules.Orders.Infrastructure.Database;

public sealed class OrdersDbContext(DbContextOptions<OrdersDbContext> options)
    : DbContext(options), IUnitOfWork
{
    internal DbSet<Order> Orders { get; set; }

    internal DbSet<Customer> Customers { get; set; }

    /// <summary>
    /// One flag per customer, replicated from Payments: whether they can be charged by card.
    /// Feature 3.8 Milestone D.
    /// </summary>
    internal DbSet<CustomerPaymentProfile> CustomerPaymentProfiles { get; set; }

    internal DbSet<Restaurant> Restaurants { get; set; }

    internal DbSet<MenuItem> MenuItems { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Outbox/inbox tables live in Common.Infrastructure, so the assembly scan below does not
        // find them — they stay explicitly applied.
        modelBuilder.ApplyConfiguration(new OutboxMessageConfiguration());
        modelBuilder.ApplyConfiguration(new OutboxMessageConsumerConfiguration());
        modelBuilder.ApplyConfiguration(new InboxMessageConfiguration());
        modelBuilder.ApplyConfiguration(new InboxMessageConsumerConfiguration());

        modelBuilder.ApplyConfigurationsFromAssembly(typeof(OrdersDbContext).Assembly);
    }
}

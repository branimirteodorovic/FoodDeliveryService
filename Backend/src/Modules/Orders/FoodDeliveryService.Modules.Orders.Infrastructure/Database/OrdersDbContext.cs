using FoodDeliveryService.Common.Infrastructure.Inbox;
using FoodDeliveryService.Common.Infrastructure.Outbox;
using FoodDeliveryService.Modules.Orders.Application.Abstractions.Data;
using FoodDeliveryService.Modules.Orders.Domain.Orders;
using Microsoft.EntityFrameworkCore;

namespace FoodDeliveryService.Modules.Orders.Infrastructure.Database;

public sealed class OrdersDbContext(DbContextOptions<OrdersDbContext> options)
    : DbContext(options), IUnitOfWork
{
    internal DbSet<Order> Orders { get; set; }


    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schemas.Orders);

        modelBuilder.ApplyConfiguration(new OutboxMessageConfiguration());
        modelBuilder.ApplyConfiguration(new OutboxMessageConsumerConfiguration());
        modelBuilder.ApplyConfiguration(new InboxMessageConfiguration());
        modelBuilder.ApplyConfiguration(new InboxMessageConsumerConfiguration());
    }
}

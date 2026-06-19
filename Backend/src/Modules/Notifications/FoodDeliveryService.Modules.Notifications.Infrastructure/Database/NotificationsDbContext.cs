using FoodDeliveryService.Common.Infrastructure.Inbox;
using FoodDeliveryService.Common.Infrastructure.Outbox;
using FoodDeliveryService.Modules.Notifications.Application.Abstractions.Data;
using FoodDeliveryService.Modules.Notifications.Domain.Notifications;
using Microsoft.EntityFrameworkCore;

namespace FoodDeliveryService.Modules.Notifications.Infrastructure.Database;

public sealed class NotificationsDbContext(DbContextOptions<NotificationsDbContext> options)
    : DbContext(options), IUnitOfWork
{
    internal DbSet<Notification> Notifications { get; set; }


    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schemas.Notifications);

        modelBuilder.ApplyConfiguration(new OutboxMessageConfiguration());
        modelBuilder.ApplyConfiguration(new OutboxMessageConsumerConfiguration());
        modelBuilder.ApplyConfiguration(new InboxMessageConfiguration());
        modelBuilder.ApplyConfiguration(new InboxMessageConsumerConfiguration());
    }
}

using FoodDeliveryService.Modules.Notifications.Domain.Notifications;
using FoodDeliveryService.Modules.Notifications.Infrastructure.Datebase;
using Microsoft.EntityFrameworkCore;

namespace FoodDeliveryService.Modules.Notifications.Infrastructure.Notifications;

internal sealed class NotificationsRepository(NotificationsDbContext context) : INotificationsRepository
{
    public async Task<Notification?> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await context.Notifications.SingleOrDefaultAsync(c => c.Id == id, cancellationToken);
    }

    public void Insert(Notification notification)
    {
        context.Notifications.Add(notification);
    }
}

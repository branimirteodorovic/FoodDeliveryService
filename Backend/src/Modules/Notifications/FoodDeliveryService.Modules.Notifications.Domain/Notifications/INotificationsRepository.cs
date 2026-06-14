namespace FoodDeliveryService.Modules.Notifications.Domain.Notifications;

public interface INotificationsRepository
{
    Task<Notification?> GetAsync(Guid id, CancellationToken cancellationToken = default);

    void Insert(Notification notification);
}

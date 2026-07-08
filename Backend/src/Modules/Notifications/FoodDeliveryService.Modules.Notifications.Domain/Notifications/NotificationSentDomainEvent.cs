using FoodDeliveryService.Common.Domain;

namespace FoodDeliveryService.Modules.Notifications.Domain.Notifications;

public sealed class NotificationSentDomainEvent(Guid notificationId) : DomainEvent
{
    public Guid NotificationId { get; init; } = notificationId;
}

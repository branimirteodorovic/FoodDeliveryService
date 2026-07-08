using FoodDeliveryService.Common.Domain;

namespace FoodDeliveryService.Modules.Notifications.Domain.Notifications;

// Notifications is a terminal consumer — it publishes no integration events. This event exists only
// for local audit completeness; no handler is registered for it.
public sealed class NotificationCreatedDomainEvent(Guid notificationId) : DomainEvent
{
    public Guid NotificationId { get; init; } = notificationId;
}

namespace FoodDeliveryService.Modules.Notifications.Domain.Notifications;

// Lifecycle of a single notification-log row: created Pending, then a terminal Sent or Failed.
public enum NotificationStatus
{
    Pending = 1,
    Sent = 2,
    Failed = 3
}

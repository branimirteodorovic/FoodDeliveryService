namespace FoodDeliveryService.Modules.Notifications.Domain.Notifications;

// The delivery channel a notification goes out on. Only Email is wired up now; Phase 2 registers
// Realtime (SignalR) and Push senders behind the same INotificationChannel seam.
public enum NotificationChannel
{
    Email = 1,
    Push = 2,
    Realtime = 3
}

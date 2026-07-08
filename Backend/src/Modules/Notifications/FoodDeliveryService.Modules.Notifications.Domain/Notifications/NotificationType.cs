namespace FoodDeliveryService.Modules.Notifications.Domain.Notifications;

// The kind of notification being sent. Only the customer's order-confirmation email exists this
// iteration; Phase-2 real-time/push (restaurant new-order alert, status changes) adds more members.
public enum NotificationType
{
    OrderConfirmation = 1
}

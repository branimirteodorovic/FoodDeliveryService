namespace FoodDeliveryService.Modules.Notifications.Domain.Notifications;

// The kind of notification being sent. Explicit values: the number is persisted on every
// notification row, so a member may be appended but never renumbered or reordered.
public enum NotificationType
{
    OrderConfirmation = 1,

    /// <summary>
    /// A support agent replied to the customer on one of their tickets. Only customer-visible agent
    /// messages get here — Support decides that before publishing, so an internal note never reaches
    /// this module at all.
    /// </summary>
    SupportTicketReply = 2
}

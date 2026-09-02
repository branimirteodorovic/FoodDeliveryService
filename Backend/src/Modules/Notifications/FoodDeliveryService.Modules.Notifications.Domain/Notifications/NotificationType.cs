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
    SupportTicketReply = 2,

    /// <summary>
    /// An administrator approved or declined a refund the customer's support agent asked for. One
    /// type for both outcomes, unlike the two integration events behind it: the customer receives
    /// one kind of message here — the answer — and splitting the type would only make "how many
    /// refund decisions did we send" a two-row query.
    /// </summary>
    RefundDecision = 3
}

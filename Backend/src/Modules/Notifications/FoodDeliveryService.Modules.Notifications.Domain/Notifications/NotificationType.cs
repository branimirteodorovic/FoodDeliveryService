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
    RefundDecision = 3,

    /// <summary>
    /// The customer's card was not charged and their order was cancelled because of it — Feature
    /// 3.8 Milestone H, §10.4.
    /// <para>
    /// Driven by Payments' <c>PaymentAuthorizationFailed</c> rather than by anything Orders
    /// publishes, because that event carries the bounded reason the email needs and a second message
    /// about one fact would carry less. It is a distinct type from an order cancellation for exactly
    /// that reason: "your card was declined" and "your order was cancelled" are not the same email
    /// even when they describe the same minute.
    /// </para>
    /// </summary>
    PaymentFailed = 4,

    /// <summary>
    /// The refund an administrator approved has actually been sent back to the card — Feature 3.8
    /// Milestone H.
    /// <para>
    /// A second email after <see cref="RefundDecision"/>, deliberately. They answer different
    /// questions — "did they agree?" and "has the money gone?" — and until this feature the platform
    /// could only ever answer the first, which is why the approval email is worded as an agreement
    /// and this one is worded as a transfer. A failed refund sends nothing: see
    /// <c>RefundFailedIntegrationEvent</c> for why the customer is not the right person to tell.
    /// </para>
    /// </summary>
    RefundSettled = 5
}

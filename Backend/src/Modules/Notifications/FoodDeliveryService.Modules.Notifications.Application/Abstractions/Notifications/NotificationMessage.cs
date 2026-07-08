namespace FoodDeliveryService.Modules.Notifications.Application.Abstractions.Notifications;

/// <summary>
/// A rendered notification ready to be dispatched on a channel. Channel-agnostic: the Email channel
/// uses <see cref="Subject"/>/<see cref="Body"/>, Phase-2 real-time/push channels use whatever subset
/// they need.
/// </summary>
public sealed record NotificationMessage(
    string RecipientEmail,
    Guid? RecipientUserId,
    string Subject,
    string Body);

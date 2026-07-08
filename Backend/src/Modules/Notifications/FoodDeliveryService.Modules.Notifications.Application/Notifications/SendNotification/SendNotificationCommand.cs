using FoodDeliveryService.Common.Application.Messaging;
using FoodDeliveryService.Modules.Notifications.Domain.Notifications;

namespace FoodDeliveryService.Modules.Notifications.Application.Notifications.SendNotification;

/// <summary>
/// Renders <paramref name="Type"/> with <paramref name="Tokens"/> and dispatches it on every channel
/// the routing map assigns to that type, logging each send to the notifications table. A transient
/// send failure marks the log row Failed and rethrows so the inbox retries.
/// </summary>
public sealed record SendNotificationCommand(
    string RecipientEmail,
    Guid? RecipientUserId,
    NotificationType Type,
    IReadOnlyDictionary<string, string> Tokens) : ICommand;

using FoodDeliveryService.Common.Application.Messaging;
using FoodDeliveryService.Modules.Notifications.Application.Abstractions.Notifications;

namespace FoodDeliveryService.Modules.Notifications.Application.Notifications.SendNotification;

/// <summary>
/// Renders <paramref name="Model"/> and dispatches it on every channel the routing map assigns to
/// <c>Model.Type</c>, logging each send to the notifications table. A transient send failure marks
/// the log row Failed and rethrows so the inbox retries.
/// </summary>
public sealed record SendNotificationCommand(
    string RecipientEmail,
    Guid? RecipientUserId,
    INotificationModel Model) : ICommand;

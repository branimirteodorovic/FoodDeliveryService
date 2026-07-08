using FoodDeliveryService.Modules.Notifications.Domain.Notifications;

namespace FoodDeliveryService.Modules.Notifications.Application.Notifications.SendNotification;

/// <summary>
/// Trivial routing map: which channels a given notification type is delivered on. This iteration
/// routes the only type (<see cref="NotificationType.OrderConfirmation"/>) to Email; Phase 2 extends
/// the map with Realtime/Push entries without touching the send pipeline.
/// </summary>
internal static class NotificationChannelRouter
{
    private static readonly Dictionary<NotificationType, IReadOnlyList<NotificationChannel>> Routes =
        new()
        {
            [NotificationType.OrderConfirmation] = [NotificationChannel.Email]
        };

    public static IReadOnlyList<NotificationChannel> Resolve(NotificationType type) =>
        Routes.TryGetValue(type, out IReadOnlyList<NotificationChannel>? channels) ? channels : [];
}

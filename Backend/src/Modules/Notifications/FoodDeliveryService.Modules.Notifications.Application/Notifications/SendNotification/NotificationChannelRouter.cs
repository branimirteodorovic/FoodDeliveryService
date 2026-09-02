using FoodDeliveryService.Modules.Notifications.Domain.Notifications;

namespace FoodDeliveryService.Modules.Notifications.Application.Notifications.SendNotification;

/// <summary>
/// Trivial routing map: which channels a given notification type is delivered on. Every type is
/// email-only today; Realtime/Push entries extend the map without touching the send pipeline.
/// <para>
/// <strong>A type missing from this map sends nothing, and says nothing about it.</strong>
/// <c>Resolve</c> returns an empty list, the send loop runs zero times and the command returns
/// success — so the inbox message is marked processed, no error is recorded anywhere, and the only
/// symptom is an email that never arrives. Adding a <see cref="NotificationType"/> and a template
/// is not enough on its own: the route here is the third half of the change.
/// </para>
/// </summary>
internal static class NotificationChannelRouter
{
    private static readonly Dictionary<NotificationType, IReadOnlyList<NotificationChannel>> Routes =
        new()
        {
            [NotificationType.OrderConfirmation] = [NotificationChannel.Email],
            [NotificationType.SupportTicketReply] = [NotificationChannel.Email],
            [NotificationType.RefundDecision] = [NotificationChannel.Email]
        };

    public static IReadOnlyList<NotificationChannel> Resolve(NotificationType type) =>
        Routes.TryGetValue(type, out IReadOnlyList<NotificationChannel>? channels) ? channels : [];
}

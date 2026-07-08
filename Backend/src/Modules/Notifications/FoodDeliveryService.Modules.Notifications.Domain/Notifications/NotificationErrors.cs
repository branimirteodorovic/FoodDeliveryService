using FoodDeliveryService.Common.Domain;

namespace FoodDeliveryService.Modules.Notifications.Domain.Notifications;

public static class NotificationErrors
{
    public static readonly Error RecipientEmailEmpty = Error.Problem(
        "Notifications.RecipientEmailEmpty",
        "A notification must have a recipient email address");

    public static Error InvalidTransition(NotificationStatus from, NotificationStatus to) =>
        Error.Problem(
            "Notifications.InvalidTransition",
            $"The notification cannot move from status {from} to status {to}");

    public static Error ChannelNotConfigured(NotificationChannel channel) =>
        Error.Problem(
            "Notifications.ChannelNotConfigured",
            $"No sender is configured for the {channel} channel");

    public static Error SendFailed(NotificationChannel channel) =>
        Error.Problem(
            "Notifications.SendFailed",
            $"Sending the notification on the {channel} channel failed");

    public static Error RecipientNotFound(Guid userId) =>
        Error.NotFound(
            "Notifications.RecipientNotFound",
            $"No recipient replica exists for user '{userId}'");
}

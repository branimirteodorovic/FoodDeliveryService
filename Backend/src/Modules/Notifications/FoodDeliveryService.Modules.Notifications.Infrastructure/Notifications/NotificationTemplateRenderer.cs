using FoodDeliveryService.Modules.Notifications.Application.Abstractions.Notifications;
using FoodDeliveryService.Modules.Notifications.Domain.Notifications;

namespace FoodDeliveryService.Modules.Notifications.Infrastructure.Notifications;

/// <summary>
/// Small in-code template registry — a switch over <see cref="NotificationType"/>. A templating engine
/// (Razor/Scriban/MJML) plus per-locale copy replaces this behind the interface later.
/// </summary>
internal sealed class NotificationTemplateRenderer : INotificationTemplateRenderer
{
    public RenderedTemplate Render(NotificationType type, IReadOnlyDictionary<string, string> tokens) =>
        type switch
        {
            NotificationType.OrderConfirmation => RenderOrderConfirmation(tokens),
            _ => throw new ArgumentOutOfRangeException(
                nameof(type),
                type,
                "No template is registered for this notification type")
        };

    private static RenderedTemplate RenderOrderConfirmation(IReadOnlyDictionary<string, string> tokens)
    {
        string firstName = tokens.GetValueOrDefault("firstName", "there");
        string restaurantName = tokens.GetValueOrDefault("restaurantName", "the restaurant");
        string orderShortId = tokens.GetValueOrDefault("orderShortId", string.Empty);
        string subtotal = tokens.GetValueOrDefault("subtotal", string.Empty);

        string subject = $"Your order {orderShortId} is confirmed";

        string body =
            $"Hi {firstName},\n\n" +
            $"Thanks for ordering from {restaurantName}. We've received your order and the restaurant " +
            $"will confirm it shortly.\n\n" +
            $"Order: {orderShortId}\n" +
            $"Subtotal: {subtotal}\n\n" +
            "You'll get live updates as your order progresses.";

        return new RenderedTemplate(subject, body);
    }
}

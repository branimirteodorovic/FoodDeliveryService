using System.Globalization;
using FoodDeliveryService.Modules.Notifications.Application.Abstractions.Notifications;

namespace FoodDeliveryService.Modules.Notifications.Infrastructure.Notifications;

/// <summary>
/// Small in-code template registry — a switch over the <see cref="INotificationModel"/> type. Each arm
/// receives a strongly-typed model, so template fields are compile-checked. A templating engine
/// (Razor/Scriban/MJML) plus per-locale copy replaces this behind the interface later.
/// </summary>
internal sealed class NotificationTemplateRenderer : INotificationTemplateRenderer
{
    public RenderedTemplate Render(INotificationModel model) =>
        model switch
        {
            OrderConfirmationModel m => RenderOrderConfirmation(m),
            _ => throw new ArgumentOutOfRangeException(
                nameof(model),
                model.GetType().Name,
                "No template is registered for this notification model")
        };

    private static RenderedTemplate RenderOrderConfirmation(OrderConfirmationModel model)
    {
        string orderShortId = model.OrderId.ToString("N")[..8].ToUpperInvariant();
        string subtotal = model.Subtotal.ToString("F2", CultureInfo.InvariantCulture);

        string subject = $"Your order {orderShortId} is confirmed";

        string body =
            $"Hi {model.FirstName},\n\n" +
            "Thanks for ordering. We've received your order and the restaurant will confirm it " +
            "shortly.\n\n" +
            $"Order: {orderShortId}\n" +
            $"Subtotal: {subtotal}\n\n" +
            "You'll get live updates as your order progresses.";

        return new RenderedTemplate(subject, body);
    }
}

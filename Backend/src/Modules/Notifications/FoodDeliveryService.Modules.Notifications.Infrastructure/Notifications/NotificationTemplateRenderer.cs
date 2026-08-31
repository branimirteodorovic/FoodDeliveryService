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
            SupportTicketReplyModel m => RenderSupportTicketReply(m),
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

    private static RenderedTemplate RenderSupportTicketReply(SupportTicketReplyModel model)
    {
        // The reference, not the ticket id: it is the identifier the customer can quote back, and
        // the subject line is where they will look for it when the thread runs to several replies.
        string subject = $"Re: {model.TicketSubject} ({model.TicketReference})";

        // The preview only. The full message stays in Support, where the customer reads it behind
        // their login — an email is not an access-controlled surface, and a support thread can carry
        // an order address or a refund decision.
        string body =
            $"Hi {model.FirstName},\n\n" +
            $"Our support team has replied to your ticket {model.TicketReference}.\n\n" +
            $"\"{model.Preview}\"\n\n" +
            "Sign in to your account to read the full message and reply.";

        return new RenderedTemplate(subject, body);
    }
}

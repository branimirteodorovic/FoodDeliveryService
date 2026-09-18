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
            RefundDecisionModel m => RenderRefundDecision(m),
            PaymentFailedModel m => RenderPaymentFailed(m),
            RefundSettledModel m => RenderRefundSettled(m),
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

    private static RenderedTemplate RenderRefundDecision(RefundDecisionModel model)
    {
        string amount = model.Amount.ToString("F2", CultureInfo.InvariantCulture);

        // The outcome is in the subject line. A customer scanning an inbox for the answer to
        // "am I getting my money back" should not have to open the message to find it.
        string subject = model.Approved
            ? $"Your refund request was approved ({model.TicketReference})"
            : $"Your refund request was declined ({model.TicketReference})";

        // "Approved", still never "arrived". Feature 3.8 made the approval move real money, but it
        // moves it asynchronously and a card refund clears at the issuer's pace — so this email
        // reports the decision and the fact that the refund is on its way, and RefundSettled is the
        // one that reports the money actually having been sent.
        string outcome = model.Approved
            ? $"Your refund request for {amount} has been approved and is being processed."
            : $"Your refund request for {amount} was not approved on this occasion.";

        string note = string.IsNullOrWhiteSpace(model.DecisionNote)
            ? string.Empty
            : $"\n\nNote from our team:\n\"{model.DecisionNote}\"";

        string body =
            $"Hi {model.FirstName},\n\n" +
            $"{outcome}\n\n" +
            $"Ticket: {model.TicketReference}" +
            note +
            "\n\nSign in to your account to see the full conversation or reply to your agent.";

        return new RenderedTemplate(subject, body);
    }

    private static RenderedTemplate RenderPaymentFailed(PaymentFailedModel model)
    {
        string orderShortId = model.OrderId.ToString("N")[..8].ToUpperInvariant();

        string subject = $"We couldn't take payment for order {orderShortId}";

        // The provider's own wording never appears here. These arms are written against Payments'
        // six bounded reason codes, and the default is the one a customer can still act on — an
        // unmapped code means this platform does not know why, and saying so beats guessing.
        string explanation = model.Reason switch
        {
            "insufficient_funds" => "Your card was declined because of insufficient funds.",
            "expired_card" => "Your card was declined because it has expired.",
            "card_declined" => "Your card was declined by your bank.",
            "authentication_required" =>
                "Your bank asked for extra verification that we couldn't complete automatically.",
            "no_payment_method" => "We couldn't find a saved card to charge.",
            _ => "We weren't able to take payment for this order."
        };

        string body =
            $"Hi {model.FirstName},\n\n" +
            $"{explanation}\n\n" +
            $"Order: {orderShortId}\n\n" +
            "Your order has been cancelled and you have not been charged. Add or update a card in " +
            "your account and you can place the order again.";

        return new RenderedTemplate(subject, body);
    }

    private static RenderedTemplate RenderRefundSettled(RefundSettledModel model)
    {
        string amount = model.Amount.ToString("F2", CultureInfo.InvariantCulture);

        string subject = $"Your refund of {amount} is on its way ({model.TicketReference})";

        // No date is promised. The refund has left this platform; when it appears on the statement
        // is the issuer's business, and "5 to 10 working days" is the honest range rather than a
        // commitment this service could keep.
        string body =
            $"Hi {model.FirstName},\n\n" +
            $"We've sent your refund of {amount} back to the card you paid with.\n\n" +
            $"Ticket: {model.TicketReference}\n\n" +
            "Refunds usually appear on your statement within 5 to 10 working days, depending on " +
            "your bank.";

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

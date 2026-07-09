namespace FoodDeliveryService.Modules.Notifications.Application.Abstractions.Notifications;

/// <summary>
/// Turns a typed <see cref="INotificationModel"/> into a rendered subject and body. Backed by a small
/// in-code registry now; a templating engine (Razor/Scriban/MJML) plus per-locale copy drops in
/// behind this interface later.
/// </summary>
public interface INotificationTemplateRenderer
{
    RenderedTemplate Render(INotificationModel model);
}

public sealed record RenderedTemplate(string Subject, string Body);

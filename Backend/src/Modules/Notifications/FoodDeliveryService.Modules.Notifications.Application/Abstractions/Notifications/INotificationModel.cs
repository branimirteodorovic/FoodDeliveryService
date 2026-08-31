using FoodDeliveryService.Modules.Notifications.Domain.Notifications;

namespace FoodDeliveryService.Modules.Notifications.Application.Abstractions.Notifications;

/// <summary>
/// The typed payload for a notification. Each concrete model declares exactly the fields its template
/// needs and which <see cref="NotificationType"/> it renders — replacing the untyped token dictionary,
/// so a mistyped field is a compile error rather than a silent default. Passed in-process via MediatR
/// (never serialized onto the bus), so a polymorphic model is safe here.
/// </summary>
public interface INotificationModel
{
    NotificationType Type { get; }
}

public sealed record OrderConfirmationModel(
    string FirstName,
    Guid OrderId,
    decimal Subtotal) : INotificationModel
{
    public NotificationType Type => NotificationType.OrderConfirmation;
}

/// <summary>
/// The agent's reply as the email renders it. <paramref name="Preview"/> is already truncated by
/// Support — this module does not hold the full message and deliberately does not ask for it, so the
/// email links the customer back to the thread rather than reproducing it.
/// </summary>
public sealed record SupportTicketReplyModel(
    string FirstName,
    string TicketReference,
    string TicketSubject,
    string Preview) : INotificationModel
{
    public NotificationType Type => NotificationType.SupportTicketReply;
}

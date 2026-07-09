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

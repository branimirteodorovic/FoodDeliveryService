using FoodDeliveryService.Modules.Notifications.Application.Abstractions.Email;
using FoodDeliveryService.Modules.Notifications.Application.Abstractions.Notifications;
using FoodDeliveryService.Modules.Notifications.Domain.Notifications;

namespace FoodDeliveryService.Modules.Notifications.Infrastructure.Notifications;

/// <summary>
/// The only channel registered this iteration: delivers a notification as an email through
/// <see cref="IEmailService"/>. Phase 2 adds SignalR/push implementations of the same interface.
/// </summary>
internal sealed class EmailNotificationChannel(IEmailService emailService) : INotificationChannel
{
    public NotificationChannel Channel => NotificationChannel.Email;

    public Task SendAsync(NotificationMessage message, CancellationToken cancellationToken = default) =>
        emailService.SendEmailAsync(message.RecipientEmail, message.Subject, message.Body, cancellationToken);
}

using FoodDeliveryService.Modules.Notifications.Domain.Notifications;

namespace FoodDeliveryService.Modules.Notifications.Application.Abstractions.Notifications;

/// <summary>
/// A single delivery channel. Only <see cref="EmailNotificationChannel"/> is registered this iteration;
/// Phase 2 adds SignalR/push implementations without touching the send pipeline. The send handler
/// receives all registered channels and routes by <see cref="Channel"/>.
/// </summary>
public interface INotificationChannel
{
    NotificationChannel Channel { get; }

    Task SendAsync(NotificationMessage message, CancellationToken cancellationToken = default);
}

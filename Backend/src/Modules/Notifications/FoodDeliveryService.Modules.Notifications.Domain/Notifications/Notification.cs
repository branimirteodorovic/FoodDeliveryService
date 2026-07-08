using FoodDeliveryService.Common.Domain;

namespace FoodDeliveryService.Modules.Notifications.Domain.Notifications;

/// <summary>
/// Audit-log aggregate for a single notification send. Created <see cref="NotificationStatus.Pending"/>,
/// then moved to a terminal <see cref="NotificationStatus.Sent"/> or <see cref="NotificationStatus.Failed"/>
/// through guarded transitions (an illegal transition returns an error, never throws). Already
/// channel-aware so Phase-2 SignalR/push logs land in the same table. Notifications is a terminal
/// consumer, so the domain events raised here have no handlers — they exist only for audit completeness.
/// </summary>
public sealed class Notification : Entity
{
    private Notification()
    {
    }

    public Guid Id { get; private set; }

    public string RecipientEmail { get; private set; }

    public Guid? RecipientUserId { get; private set; }

    public NotificationType Type { get; private set; }

    public NotificationChannel Channel { get; private set; }

    public string Subject { get; private set; }

    public NotificationStatus Status { get; private set; }

    public string? Error { get; private set; }

    public DateTime CreatedOnUtc { get; private set; }

    public DateTime? SentOnUtc { get; private set; }

    public static Result<Notification> Create(
        string recipientEmail,
        Guid? recipientUserId,
        NotificationType type,
        NotificationChannel channel,
        string subject,
        DateTime utcNow)
    {
        if (string.IsNullOrWhiteSpace(recipientEmail))
        {
            return Result.Failure<Notification>(NotificationErrors.RecipientEmailEmpty);
        }

        var notification = new Notification
        {
            Id = Guid.NewGuid(),
            RecipientEmail = recipientEmail,
            RecipientUserId = recipientUserId,
            Type = type,
            Channel = channel,
            Subject = subject,
            Status = NotificationStatus.Pending,
            CreatedOnUtc = utcNow
        };

        notification.Raise(new NotificationCreatedDomainEvent(notification.Id));

        return notification;
    }

    public Result MarkSent(DateTime utcNow)
    {
        if (Status != NotificationStatus.Pending)
        {
            return Result.Failure(NotificationErrors.InvalidTransition(Status, NotificationStatus.Sent));
        }

        Status = NotificationStatus.Sent;
        SentOnUtc = utcNow;
        Error = null;

        Raise(new NotificationSentDomainEvent(Id));

        return Result.Success();
    }

    public Result MarkFailed(string error)
    {
        if (Status != NotificationStatus.Pending)
        {
            return Result.Failure(NotificationErrors.InvalidTransition(Status, NotificationStatus.Failed));
        }

        Status = NotificationStatus.Failed;
        Error = error;

        Raise(new NotificationFailedDomainEvent(Id));

        return Result.Success();
    }
}

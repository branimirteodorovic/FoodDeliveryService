using FoodDeliveryService.Common.Application.Clock;
using FoodDeliveryService.Common.Application.Messaging;
using FoodDeliveryService.Common.Domain;
using FoodDeliveryService.Modules.Notifications.Application.Abstractions.Data;
using FoodDeliveryService.Modules.Notifications.Application.Abstractions.Notifications;
using FoodDeliveryService.Modules.Notifications.Domain.Notifications;

namespace FoodDeliveryService.Modules.Notifications.Application.Notifications.SendNotification;

internal sealed class SendNotificationCommandHandler(
    INotificationsRepository notificationsRepository,
    INotificationTemplateRenderer templateRenderer,
    IEnumerable<INotificationChannel> channels,
    IDateTimeProvider dateTimeProvider,
    IUnitOfWork unitOfWork)
    : ICommandHandler<SendNotificationCommand>
{
    public async Task<Result> Handle(SendNotificationCommand request, CancellationToken cancellationToken)
    {
        IReadOnlyList<NotificationChannel> targetChannels = NotificationChannelRouter.Resolve(request.Type);

        RenderedTemplate template = templateRenderer.Render(request.Type, request.Tokens);

        foreach (NotificationChannel targetChannel in targetChannels)
        {
            Result result = await SendOnChannelAsync(request, template, targetChannel, cancellationToken);

            if (result.IsFailure)
            {
                return result;
            }
        }

        return Result.Success();
    }

    private async Task<Result> SendOnChannelAsync(
        SendNotificationCommand request,
        RenderedTemplate template,
        NotificationChannel targetChannel,
        CancellationToken cancellationToken)
    {
        INotificationChannel? channel = channels.SingleOrDefault(c => c.Channel == targetChannel);

        if (channel is null)
        {
            return Result.Failure(NotificationErrors.ChannelNotConfigured(targetChannel));
        }

        Result<Notification> notificationResult = Notification.Create(
            request.RecipientEmail,
            request.RecipientUserId,
            request.Type,
            targetChannel,
            template.Subject,
            dateTimeProvider.UtcNow);

        if (notificationResult.IsFailure)
        {
            return Result.Failure(notificationResult.Error);
        }

        Notification notification = notificationResult.Value;

        // Persist Pending first so a crash mid-send still leaves an audit trail.
        notificationsRepository.Insert(notification);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        try
        {
            await channel.SendAsync(
                new NotificationMessage(
                    request.RecipientEmail,
                    request.RecipientUserId,
                    template.Subject,
                    template.Body),
                cancellationToken);
        }
        catch (Exception exception)
        {
            notification.MarkFailed(exception.Message);
            await unitOfWork.SaveChangesAsync(cancellationToken);

            // Rethrow so the inbox leaves the message unprocessed and ProcessInboxJob retries the send.
            throw new Common.Application.Exceptions.ApplicationException(
                nameof(SendNotificationCommand),
                NotificationErrors.SendFailed(targetChannel),
                exception);
        }

        notification.MarkSent(dateTimeProvider.UtcNow);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}

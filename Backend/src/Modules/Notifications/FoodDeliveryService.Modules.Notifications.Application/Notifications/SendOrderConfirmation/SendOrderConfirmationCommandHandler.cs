using System.Globalization;
using FoodDeliveryService.Common.Application.Messaging;
using FoodDeliveryService.Common.Domain;
using FoodDeliveryService.Modules.Notifications.Application.Notifications.SendNotification;
using FoodDeliveryService.Modules.Notifications.Domain.Notifications;
using FoodDeliveryService.Modules.Notifications.Domain.RecipientUsers;
using MediatR;

namespace FoodDeliveryService.Modules.Notifications.Application.Notifications.SendOrderConfirmation;

internal sealed class SendOrderConfirmationCommandHandler(
    IRecipientUserRepository recipientUserRepository,
    ISender sender)
    : ICommandHandler<SendOrderConfirmationCommand>
{
    public async Task<Result> Handle(SendOrderConfirmationCommand request, CancellationToken cancellationToken)
    {
        RecipientUser? recipient = await recipientUserRepository.GetAsync(request.CustomerId, cancellationToken);

        // The user's registration event may still be in flight. Failing here leaves the inbox message
        // unprocessed so ProcessInboxJob retries rather than silently dropping the confirmation email.
        if (recipient is null)
        {
            return Result.Failure(NotificationErrors.RecipientNotFound(request.CustomerId));
        }

        var tokens = new Dictionary<string, string>
        {
            ["firstName"] = recipient.FirstName,
            ["orderShortId"] = request.OrderId.ToString("N")[..8].ToUpperInvariant(),
            ["subtotal"] = request.Subtotal.ToString("F2", CultureInfo.InvariantCulture)
        };

        return await sender.Send(
            new SendNotificationCommand(
                recipient.Email,
                recipient.Id,
                NotificationType.OrderConfirmation,
                tokens),
            cancellationToken);
    }
}

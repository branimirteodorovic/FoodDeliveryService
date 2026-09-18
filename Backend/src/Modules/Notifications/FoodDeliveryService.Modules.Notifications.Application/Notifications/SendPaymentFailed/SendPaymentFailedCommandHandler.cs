using FoodDeliveryService.Common.Application.Messaging;
using FoodDeliveryService.Common.Domain;
using FoodDeliveryService.Modules.Notifications.Application.Abstractions.Notifications;
using FoodDeliveryService.Modules.Notifications.Application.Notifications.SendNotification;
using FoodDeliveryService.Modules.Notifications.Domain.Notifications;
using FoodDeliveryService.Modules.Notifications.Domain.RecipientUsers;
using MediatR;

namespace FoodDeliveryService.Modules.Notifications.Application.Notifications.SendPaymentFailed;

internal sealed class SendPaymentFailedCommandHandler(
    IRecipientUserRepository recipientUserRepository,
    ISender sender)
    : ICommandHandler<SendPaymentFailedCommand>
{
    public async Task<Result> Handle(SendPaymentFailedCommand request, CancellationToken cancellationToken)
    {
        RecipientUser? recipient = await recipientUserRepository.GetAsync(request.CustomerId, cancellationToken);

        // Failing leaves the inbox message unprocessed so ProcessInboxJob retries, rather than
        // silently dropping the one message that explains why an order the customer placed has
        // vanished. The address is never resolved by calling Users — the replica is the contract.
        if (recipient is null)
        {
            return Result.Failure(NotificationErrors.RecipientNotFound(request.CustomerId));
        }

        return await sender.Send(
            new SendNotificationCommand(
                recipient.Email,
                recipient.Id,
                new PaymentFailedModel(
                    recipient.FirstName,
                    request.OrderId,
                    request.Reason)),
            cancellationToken);
    }
}

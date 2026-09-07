using FoodDeliveryService.Common.Application.Messaging;
using FoodDeliveryService.Common.Domain;
using FoodDeliveryService.Modules.Notifications.Application.Abstractions.Notifications;
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

        // The user's registration event may still be in flight. Failing here surfaces that on the inbox
        // row's error column — ProcessInboxJob records it and moves on, it does not retry.
        if (recipient is null)
        {
            return Result.Failure(NotificationErrors.RecipientNotFound(request.CustomerId));
        }

        return await sender.Send(
            new SendNotificationCommand(
                recipient.Email,
                recipient.Id,
                new OrderConfirmationModel(recipient.FirstName, request.OrderId, request.Subtotal)),
            cancellationToken);
    }
}

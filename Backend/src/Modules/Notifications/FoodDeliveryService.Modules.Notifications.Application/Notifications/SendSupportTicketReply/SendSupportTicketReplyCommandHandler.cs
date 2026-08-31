using FoodDeliveryService.Common.Application.Messaging;
using FoodDeliveryService.Common.Domain;
using FoodDeliveryService.Modules.Notifications.Application.Abstractions.Notifications;
using FoodDeliveryService.Modules.Notifications.Application.Notifications.SendNotification;
using FoodDeliveryService.Modules.Notifications.Domain.Notifications;
using FoodDeliveryService.Modules.Notifications.Domain.RecipientUsers;
using MediatR;

namespace FoodDeliveryService.Modules.Notifications.Application.Notifications.SendSupportTicketReply;

internal sealed class SendSupportTicketReplyCommandHandler(
    IRecipientUserRepository recipientUserRepository,
    ISender sender)
    : ICommandHandler<SendSupportTicketReplyCommand>
{
    public async Task<Result> Handle(SendSupportTicketReplyCommand request, CancellationToken cancellationToken)
    {
        RecipientUser? recipient = await recipientUserRepository.GetAsync(request.CustomerId, cancellationToken);

        // Failing here leaves the inbox message unprocessed so ProcessInboxJob retries, rather than
        // dropping a reply the customer is waiting on. The address is never resolved by calling
        // Users — the replica is the contract.
        if (recipient is null)
        {
            return Result.Failure(NotificationErrors.RecipientNotFound(request.CustomerId));
        }

        return await sender.Send(
            new SendNotificationCommand(
                recipient.Email,
                recipient.Id,
                new SupportTicketReplyModel(
                    recipient.FirstName,
                    request.TicketReference,
                    request.TicketSubject,
                    request.Preview)),
            cancellationToken);
    }
}

using FoodDeliveryService.Common.Application.Messaging;
using FoodDeliveryService.Common.Domain;
using FoodDeliveryService.Modules.Notifications.Application.Abstractions.Notifications;
using FoodDeliveryService.Modules.Notifications.Application.Notifications.SendNotification;
using FoodDeliveryService.Modules.Notifications.Domain.Notifications;
using FoodDeliveryService.Modules.Notifications.Domain.RecipientUsers;
using MediatR;

namespace FoodDeliveryService.Modules.Notifications.Application.Notifications.SendRefundDecision;

internal sealed class SendRefundDecisionCommandHandler(
    IRecipientUserRepository recipientUserRepository,
    ISender sender)
    : ICommandHandler<SendRefundDecisionCommand>
{
    public async Task<Result> Handle(SendRefundDecisionCommand request, CancellationToken cancellationToken)
    {
        RecipientUser? recipient = await recipientUserRepository.GetAsync(request.CustomerId, cancellationToken);

        // Failing leaves the inbox message unprocessed so ProcessInboxJob retries, rather than
        // silently dropping the answer to a refund the customer is waiting on. The address is never
        // resolved by calling Users — the replica is the contract.
        if (recipient is null)
        {
            return Result.Failure(NotificationErrors.RecipientNotFound(request.CustomerId));
        }

        return await sender.Send(
            new SendNotificationCommand(
                recipient.Email,
                recipient.Id,
                new RefundDecisionModel(
                    recipient.FirstName,
                    request.TicketReference,
                    request.Amount,
                    request.Approved,
                    request.DecisionNote)),
            cancellationToken);
    }
}

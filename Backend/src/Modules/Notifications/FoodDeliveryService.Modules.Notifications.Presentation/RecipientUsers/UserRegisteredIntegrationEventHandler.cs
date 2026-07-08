using FoodDeliveryService.Common.Application.EventBus;
using FoodDeliveryService.Common.Domain;
using FoodDeliveryService.Modules.Notifications.Application.RecipientUsers.UpsertRecipientUser;
using FoodDeliveryService.Modules.Users.IntegrationEvents;
using MediatR;

namespace FoodDeliveryService.Modules.Notifications.Presentation.RecipientUsers;

/// <summary>
/// Maintains the local RecipientUser replica (dispatched by ProcessInboxJob, idempotent via the
/// inbox). Unlike the Orders Customer replica this keeps every role — Phase-2 real-time/push must
/// resolve managers/drivers too — so there is no role filter here.
/// </summary>
internal sealed class UserRegisteredIntegrationEventHandler(ISender sender)
    : IntegrationEventHandler<UserRegisteredIntegrationEvent>
{
    public override async Task Handle(
        UserRegisteredIntegrationEvent integrationEvent,
        CancellationToken cancellationToken = default)
    {
        Result result = await sender.Send(
            new UpsertRecipientUserCommand(
                integrationEvent.UserId,
                integrationEvent.Email,
                integrationEvent.FirstName,
                integrationEvent.LastName),
            cancellationToken);

        if (result.IsFailure)
        {
            throw new Common.Application.Exceptions.ApplicationException(
                nameof(UpsertRecipientUserCommand),
                result.Error);
        }
    }
}

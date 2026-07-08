using FoodDeliveryService.Common.Application.EventBus;
using FoodDeliveryService.Common.Domain;
using FoodDeliveryService.Modules.Notifications.Application.RecipientUsers.UpdateRecipientUser;
using FoodDeliveryService.Modules.Users.IntegrationEvents;
using MediatR;

namespace FoodDeliveryService.Modules.Notifications.Presentation.RecipientUsers;

/// <summary>
/// Keeps the RecipientUser replica's name in sync. The command no-ops for a user not yet replicated
/// here (the register event may still be in flight), so every profile update can be consumed safely.
/// </summary>
internal sealed class UserProfileUpdatedIntegrationEventHandler(ISender sender)
    : IntegrationEventHandler<UserProfileUpdatedIntegrationEvent>
{
    public override async Task Handle(
        UserProfileUpdatedIntegrationEvent integrationEvent,
        CancellationToken cancellationToken = default)
    {
        Result result = await sender.Send(
            new UpdateRecipientUserCommand(
                integrationEvent.UserId,
                integrationEvent.FirstName,
                integrationEvent.LastName),
            cancellationToken);

        if (result.IsFailure)
        {
            throw new Common.Application.Exceptions.ApplicationException(
                nameof(UpdateRecipientUserCommand),
                result.Error);
        }
    }
}

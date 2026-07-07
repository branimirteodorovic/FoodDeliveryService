using FoodDeliveryService.Common.Application.EventBus;
using FoodDeliveryService.Common.Domain;
using FoodDeliveryService.Modules.Restaurants.Application.Managers.UpdateManager;
using FoodDeliveryService.Modules.Users.IntegrationEvents;
using MediatR;

namespace FoodDeliveryService.Modules.Restaurants.Presentation.Managers;

/// <summary>
/// Keeps the RestaurantManager replica's name in sync. The command no-ops for users that are not
/// replicated here (e.g. customers), so every profile update can be consumed safely.
/// </summary>
internal sealed class UserProfileUpdatedIntegrationEventHandler(ISender sender)
    : IntegrationEventHandler<UserProfileUpdatedIntegrationEvent>
{
    public override async Task Handle(
        UserProfileUpdatedIntegrationEvent integrationEvent,
        CancellationToken cancellationToken = default)
    {
        Result result = await sender.Send(
            new UpdateRestaurantManagerCommand(
                integrationEvent.UserId,
                integrationEvent.FirstName,
                integrationEvent.LastName),
            cancellationToken);

        if (result.IsFailure)
        {
            throw new Common.Application.Exceptions.ApplicationException(
                nameof(UpdateRestaurantManagerCommand),
                result.Error);
        }
    }
}

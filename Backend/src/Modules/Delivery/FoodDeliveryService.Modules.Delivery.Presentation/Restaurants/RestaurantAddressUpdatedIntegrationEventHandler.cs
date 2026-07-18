using FoodDeliveryService.Common.Application.EventBus;
using FoodDeliveryService.Common.Domain;
using FoodDeliveryService.Modules.Delivery.Application.Restaurants.UpsertRestaurant;
using FoodDeliveryService.Modules.Restaurants.IntegrationEvents;
using MediatR;

namespace FoodDeliveryService.Modules.Delivery.Presentation.Restaurants;

/// <summary>
/// Keeps the Restaurant replica's coordinates current when a restaurant moves, so a re-offer uses
/// the up-to-date pickup point. Same upsert as registration — full snapshot, idempotent.
/// </summary>
internal sealed class RestaurantAddressUpdatedIntegrationEventHandler(ISender sender)
    : IntegrationEventHandler<RestaurantAddressUpdatedIntegrationEvent>
{
    public override async Task Handle(
        RestaurantAddressUpdatedIntegrationEvent integrationEvent,
        CancellationToken cancellationToken = default)
    {
        Result result = await sender.Send(
            new UpsertRestaurantCommand(
                integrationEvent.RestaurantId,
                integrationEvent.Name,
                integrationEvent.Latitude,
                integrationEvent.Longitude),
            cancellationToken);

        if (result.IsFailure)
        {
            throw new Common.Application.Exceptions.ApplicationException(
                nameof(UpsertRestaurantCommand),
                result.Error);
        }
    }
}

using FoodDeliveryService.Common.Application.EventBus;
using FoodDeliveryService.Common.Domain;
using FoodDeliveryService.Modules.Delivery.Application.Restaurants.UpsertRestaurant;
using FoodDeliveryService.Modules.Restaurants.IntegrationEvents;
using MediatR;

namespace FoodDeliveryService.Modules.Delivery.Presentation.Restaurants;

/// <summary>
/// Maintains the local Restaurant replica (dispatched by ProcessInboxJob, idempotent via the
/// inbox). The pickup coordinates back a re-offer after a restaurant moves.
/// </summary>
internal sealed class RestaurantRegisteredIntegrationEventHandler(ISender sender)
    : IntegrationEventHandler<RestaurantRegisteredIntegrationEvent>
{
    public override async Task Handle(
        RestaurantRegisteredIntegrationEvent integrationEvent,
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

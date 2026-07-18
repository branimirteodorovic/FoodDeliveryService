using FoodDeliveryService.Common.Application.EventBus;
using FoodDeliveryService.Common.Domain;
using FoodDeliveryService.Modules.Orders.Application.Restaurants.UpsertRestaurant;
using FoodDeliveryService.Modules.Restaurants.IntegrationEvents;
using MediatR;

namespace FoodDeliveryService.Modules.Orders.Presentation.Restaurants;

/// <summary>
/// Maintains the local Restaurant replica (dispatched by ProcessInboxJob, idempotent via the
/// inbox). The snapshot's manager id backs ownership checks on order transitions and the
/// commission rate is copied onto each order at placement.
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
                integrationEvent.ManagerUserId,
                integrationEvent.Name,
                integrationEvent.CommissionRate,
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

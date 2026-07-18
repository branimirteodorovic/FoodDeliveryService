using FoodDeliveryService.Common.Application.EventBus;
using FoodDeliveryService.Common.Application.Messaging;
using FoodDeliveryService.Common.Domain;
using FoodDeliveryService.Modules.Restaurants.Application.Restaurants.GetRestaurant;
using FoodDeliveryService.Modules.Restaurants.Domain.Restaurants;
using FoodDeliveryService.Modules.Restaurants.IntegrationEvents;
using MediatR;

namespace FoodDeliveryService.Modules.Restaurants.Application.Restaurants.UpdateRestaurant;

// Runs from ProcessOutboxJob (idempotent). Publishes the full-snapshot address event so the
// Delivery service keeps its restaurant replica — and therefore the pickup point used for
// re-offers — current without calling back.
internal sealed class RestaurantAddressUpdatedDomainEventHandler(ISender sender, IEventBus eventBus)
    : DomainEventHandler<RestaurantAddressUpdatedDomainEvent>
{
    public override async Task Handle(
        RestaurantAddressUpdatedDomainEvent domainEvent,
        CancellationToken cancellationToken = default)
    {
        Result<RestaurantResponse> result = await sender.Send(
            new GetRestaurantQuery(domainEvent.RestaurantId),
            cancellationToken);

        if (result.IsFailure)
        {
            throw new Common.Application.Exceptions.ApplicationException(nameof(GetRestaurantQuery), result.Error);
        }

        await eventBus.PublishAsync(
            new RestaurantAddressUpdatedIntegrationEvent(
                domainEvent.Id,
                domainEvent.OccurredOnUtc,
                result.Value.Id,
                result.Value.Name,
                result.Value.Street,
                result.Value.City,
                result.Value.PostalCode,
                result.Value.Country,
                result.Value.Latitude.GetValueOrDefault(),
                result.Value.Longitude.GetValueOrDefault()),
            cancellationToken);
    }
}

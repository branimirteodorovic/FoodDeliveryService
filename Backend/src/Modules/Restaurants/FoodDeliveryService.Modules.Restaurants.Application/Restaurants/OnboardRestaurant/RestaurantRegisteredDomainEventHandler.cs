using FoodDeliveryService.Common.Application.EventBus;
using FoodDeliveryService.Common.Application.Messaging;
using FoodDeliveryService.Common.Domain;
using FoodDeliveryService.Modules.Restaurants.Application.Restaurants.GetRestaurant;
using FoodDeliveryService.Modules.Restaurants.Domain.Restaurants;
using FoodDeliveryService.Modules.Restaurants.IntegrationEvents;
using MediatR;

namespace FoodDeliveryService.Modules.Restaurants.Application.Restaurants.OnboardRestaurant;

// Runs from ProcessOutboxJob (idempotent). Publishes the full-snapshot integration event so future
// consumers (Orders, Notifications, search) can react without calling back into this service.
internal sealed class RestaurantRegisteredDomainEventHandler(ISender sender, IEventBus eventBus)
    : DomainEventHandler<RestaurantRegisteredDomainEvent>
{
    public override async Task Handle(
        RestaurantRegisteredDomainEvent domainEvent,
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
            new RestaurantRegisteredIntegrationEvent(
                domainEvent.Id,
                domainEvent.OccurredOnUtc,
                result.Value.Id,
                result.Value.ManagerUserId,
                result.Value.Name,
                result.Value.CuisineType,
                result.Value.Street,
                result.Value.City,
                result.Value.PostalCode,
                result.Value.Country,
                result.Value.Latitude.GetValueOrDefault(),
                result.Value.Longitude.GetValueOrDefault(),
                result.Value.CommissionRate),
            cancellationToken);
    }
}

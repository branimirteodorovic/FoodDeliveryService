using FoodDeliveryService.Common.Application.EventBus;
using FoodDeliveryService.Common.Application.Messaging;
using FoodDeliveryService.Common.Domain;
using FoodDeliveryService.Modules.Restaurants.Application.Restaurants.GetMenuItem;
using FoodDeliveryService.Modules.Restaurants.Domain.Restaurants;
using FoodDeliveryService.Modules.Restaurants.IntegrationEvents;
using MediatR;

namespace FoodDeliveryService.Modules.Restaurants.Application.Restaurants.CreateMenuItem;

// Runs from ProcessOutboxJob (idempotent). Publishes the full-snapshot integration event so Orders
// can seed its menu replica without calling back into this service.
internal sealed class MenuItemAddedDomainEventHandler(ISender sender, IEventBus eventBus)
    : DomainEventHandler<MenuItemAddedDomainEvent>
{
    public override async Task Handle(
        MenuItemAddedDomainEvent domainEvent,
        CancellationToken cancellationToken = default)
    {
        Result<MenuItemSnapshotResponse> result = await sender.Send(
            new GetMenuItemQuery(domainEvent.MenuItemId),
            cancellationToken);

        if (result.IsFailure)
        {
            throw new Common.Application.Exceptions.ApplicationException(nameof(GetMenuItemQuery), result.Error);
        }

        await eventBus.PublishAsync(
            new MenuItemAddedIntegrationEvent(
                domainEvent.Id,
                domainEvent.OccurredOnUtc,
                result.Value.RestaurantId,
                result.Value.Id,
                result.Value.Name,
                result.Value.Price,
                result.Value.IsAvailable),
            cancellationToken);
    }
}

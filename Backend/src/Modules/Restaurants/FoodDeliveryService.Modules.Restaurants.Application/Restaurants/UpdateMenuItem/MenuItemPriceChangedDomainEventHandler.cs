using FoodDeliveryService.Common.Application.EventBus;
using FoodDeliveryService.Common.Application.Messaging;
using FoodDeliveryService.Common.Domain;
using FoodDeliveryService.Modules.Restaurants.Application.Restaurants.GetMenuItem;
using FoodDeliveryService.Modules.Restaurants.Domain.Restaurants;
using FoodDeliveryService.Modules.Restaurants.IntegrationEvents;
using MediatR;

namespace FoodDeliveryService.Modules.Restaurants.Application.Restaurants.UpdateMenuItem;

// Runs from ProcessOutboxJob (idempotent). Price changes collapse onto the same full-snapshot
// MenuItemUpdatedIntegrationEvent as detail changes — consumers upsert the whole item either way,
// so they never need to distinguish which field moved.
internal sealed class MenuItemPriceChangedDomainEventHandler(ISender sender, IEventBus eventBus)
    : DomainEventHandler<MenuItemPriceChangedDomainEvent>
{
    public override async Task Handle(
        MenuItemPriceChangedDomainEvent domainEvent,
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
            new MenuItemUpdatedIntegrationEvent(
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

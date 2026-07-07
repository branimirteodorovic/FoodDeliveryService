using FoodDeliveryService.Common.Application.EventBus;
using FoodDeliveryService.Common.Application.Messaging;
using FoodDeliveryService.Common.Domain;
using FoodDeliveryService.Modules.Restaurants.Application.Restaurants.GetMenuItem;
using FoodDeliveryService.Modules.Restaurants.Domain.Restaurants;
using FoodDeliveryService.Modules.Restaurants.IntegrationEvents;
using MediatR;

namespace FoodDeliveryService.Modules.Restaurants.Application.Restaurants.UpdateMenuItem;

// Runs from ProcessOutboxJob (idempotent). Detail changes publish the same full-snapshot
// MenuItemUpdatedIntegrationEvent as price changes, so consumer replicas always receive the whole
// current item.
internal sealed class MenuItemUpdatedDomainEventHandler(ISender sender, IEventBus eventBus)
    : DomainEventHandler<MenuItemUpdatedDomainEvent>
{
    public override async Task Handle(
        MenuItemUpdatedDomainEvent domainEvent,
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

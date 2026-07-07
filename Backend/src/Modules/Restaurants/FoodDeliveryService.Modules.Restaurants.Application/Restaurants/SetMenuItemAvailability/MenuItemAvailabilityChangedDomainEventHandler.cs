using FoodDeliveryService.Common.Application.EventBus;
using FoodDeliveryService.Common.Application.Messaging;
using FoodDeliveryService.Modules.Restaurants.Domain.Restaurants;
using FoodDeliveryService.Modules.Restaurants.IntegrationEvents;

namespace FoodDeliveryService.Modules.Restaurants.Application.Restaurants.SetMenuItemAvailability;

// Runs from ProcessOutboxJob (idempotent). The domain event already carries the full availability
// snapshot (restaurant, item, flag), so no read-back is needed before publishing.
internal sealed class MenuItemAvailabilityChangedDomainEventHandler(IEventBus eventBus)
    : DomainEventHandler<MenuItemAvailabilityChangedDomainEvent>
{
    public override async Task Handle(
        MenuItemAvailabilityChangedDomainEvent domainEvent,
        CancellationToken cancellationToken = default)
    {
        await eventBus.PublishAsync(
            new MenuItemAvailabilityChangedIntegrationEvent(
                domainEvent.Id,
                domainEvent.OccurredOnUtc,
                domainEvent.RestaurantId,
                domainEvent.MenuItemId,
                domainEvent.IsAvailable),
            cancellationToken);
    }
}

using FoodDeliveryService.Common.Application.EventBus;

namespace FoodDeliveryService.Modules.Restaurants.IntegrationEvents;

/// <summary>
/// Published when a menu item's details or price change. One full snapshot covers both — the
/// MenuItemUpdated and MenuItemPriceChanged domain events collapse onto this contract so consumer
/// replicas always stay whole instead of patching single fields.
/// </summary>
public sealed class MenuItemUpdatedIntegrationEvent : IntegrationEvent
{
    public MenuItemUpdatedIntegrationEvent(
        Guid id,
        DateTime occurredOnUtc,
        Guid restaurantId,
        Guid menuItemId,
        string name,
        decimal price,
        bool isAvailable)
        : base(id, occurredOnUtc)
    {
        RestaurantId = restaurantId;
        MenuItemId = menuItemId;
        Name = name;
        Price = price;
        IsAvailable = isAvailable;
    }

    public Guid RestaurantId { get; init; }

    public Guid MenuItemId { get; init; }

    public string Name { get; init; }

    public decimal Price { get; init; }

    public bool IsAvailable { get; init; }
}

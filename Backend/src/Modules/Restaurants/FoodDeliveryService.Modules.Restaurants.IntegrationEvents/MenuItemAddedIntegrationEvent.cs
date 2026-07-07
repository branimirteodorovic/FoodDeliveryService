using FoodDeliveryService.Common.Application.EventBus;

namespace FoodDeliveryService.Modules.Restaurants.IntegrationEvents;

/// <summary>
/// Published when a manager adds a menu item. Full snapshot — consumers (Orders keeps a local
/// menu replica for server-side pricing) must never call back for data.
/// </summary>
public sealed class MenuItemAddedIntegrationEvent : IntegrationEvent
{
    public MenuItemAddedIntegrationEvent(
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

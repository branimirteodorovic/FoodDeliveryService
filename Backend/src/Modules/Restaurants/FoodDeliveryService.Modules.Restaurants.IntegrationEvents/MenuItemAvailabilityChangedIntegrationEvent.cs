using FoodDeliveryService.Common.Application.EventBus;

namespace FoodDeliveryService.Modules.Restaurants.IntegrationEvents;

/// <summary>
/// Published when a manager toggles a menu item on/off the menu. Orders uses it to keep its menu
/// replica's availability current so placement can reject unavailable items.
/// </summary>
public sealed class MenuItemAvailabilityChangedIntegrationEvent : IntegrationEvent
{
    public MenuItemAvailabilityChangedIntegrationEvent(
        Guid id,
        DateTime occurredOnUtc,
        Guid restaurantId,
        Guid menuItemId,
        bool isAvailable)
        : base(id, occurredOnUtc)
    {
        RestaurantId = restaurantId;
        MenuItemId = menuItemId;
        IsAvailable = isAvailable;
    }

    public Guid RestaurantId { get; init; }

    public Guid MenuItemId { get; init; }

    public bool IsAvailable { get; init; }
}

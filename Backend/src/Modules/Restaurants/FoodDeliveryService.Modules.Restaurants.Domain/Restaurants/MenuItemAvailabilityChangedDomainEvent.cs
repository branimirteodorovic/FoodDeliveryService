using FoodDeliveryService.Common.Domain;

namespace FoodDeliveryService.Modules.Restaurants.Domain.Restaurants;

public sealed class MenuItemAvailabilityChangedDomainEvent(Guid restaurantId, Guid menuItemId, bool isAvailable)
    : DomainEvent
{
    public Guid RestaurantId { get; init; } = restaurantId;

    public Guid MenuItemId { get; init; } = menuItemId;

    public bool IsAvailable { get; init; } = isAvailable;
}

using FoodDeliveryService.Common.Domain;

namespace FoodDeliveryService.Modules.Restaurants.Domain.Restaurants;

public sealed class MenuItemAddedDomainEvent(Guid restaurantId, Guid menuItemId) : DomainEvent
{
    public Guid RestaurantId { get; init; } = restaurantId;

    public Guid MenuItemId { get; init; } = menuItemId;
}

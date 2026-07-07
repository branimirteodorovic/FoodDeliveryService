using FoodDeliveryService.Common.Domain;

namespace FoodDeliveryService.Modules.Restaurants.Domain.Restaurants;

public sealed class MenuItemPriceChangedDomainEvent(Guid restaurantId, Guid menuItemId, decimal price) : DomainEvent
{
    public Guid RestaurantId { get; init; } = restaurantId;

    public Guid MenuItemId { get; init; } = menuItemId;

    public decimal Price { get; init; } = price;
}

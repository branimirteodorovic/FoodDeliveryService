using FoodDeliveryService.Common.Domain;

namespace FoodDeliveryService.Modules.Restaurants.Domain.Restaurants;

public sealed class RestaurantAddressUpdatedDomainEvent(Guid restaurantId) : DomainEvent
{
    public Guid RestaurantId { get; init; } = restaurantId;
}

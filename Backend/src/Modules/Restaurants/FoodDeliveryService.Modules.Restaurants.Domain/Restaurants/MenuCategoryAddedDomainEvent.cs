using FoodDeliveryService.Common.Domain;

namespace FoodDeliveryService.Modules.Restaurants.Domain.Restaurants;

public sealed class MenuCategoryAddedDomainEvent(Guid restaurantId, Guid categoryId) : DomainEvent
{
    public Guid RestaurantId { get; init; } = restaurantId;

    public Guid CategoryId { get; init; } = categoryId;
}

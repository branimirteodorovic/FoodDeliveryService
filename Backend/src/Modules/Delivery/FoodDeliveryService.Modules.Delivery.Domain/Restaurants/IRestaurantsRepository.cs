namespace FoodDeliveryService.Modules.Delivery.Domain.Restaurants;

public interface IRestaurantsRepository
{
    Task<Restaurant?> GetAsync(Guid restaurantId, CancellationToken cancellationToken = default);

    void Insert(Restaurant restaurant);
}

namespace FoodDeliveryService.Modules.Restaurants.Domain.Restaurants;

public interface IRestaurantsRepository
{
    Task<Restaurant?> GetAsync(Guid id, CancellationToken cancellationToken = default);

    void Insert(Restaurant restaurant);
}

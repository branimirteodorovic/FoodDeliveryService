namespace FoodDeliveryService.Modules.Restaurants.Domain.Restaurants;

public interface IRestaurantsRepository
{
    // Loads the full aggregate (menu categories + items) — menu writes go through the root.
    Task<Restaurant?> GetAsync(Guid id, CancellationToken cancellationToken = default);

    void Insert(Restaurant restaurant);
}

namespace FoodDeliveryService.Modules.Restaurants.Domain.Managers;

public interface IRestaurantManagersRepository
{
    Task<RestaurantManager?> GetAsync(Guid id, CancellationToken cancellationToken = default);

    void Insert(RestaurantManager manager);
}

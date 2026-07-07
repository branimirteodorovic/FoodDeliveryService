namespace FoodDeliveryService.Modules.Orders.Domain.Restaurants;

public interface IRestaurantReplicaRepository
{
    Task<Restaurant?> GetAsync(Guid id, CancellationToken cancellationToken = default);

    void Insert(Restaurant restaurant);
}

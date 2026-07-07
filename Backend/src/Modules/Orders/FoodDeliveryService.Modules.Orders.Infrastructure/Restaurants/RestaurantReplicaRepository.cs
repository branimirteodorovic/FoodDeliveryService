using FoodDeliveryService.Modules.Orders.Domain.Restaurants;
using FoodDeliveryService.Modules.Orders.Infrastructure.Database;
using Microsoft.EntityFrameworkCore;

namespace FoodDeliveryService.Modules.Orders.Infrastructure.Restaurants;

internal sealed class RestaurantReplicaRepository(OrdersDbContext context) : IRestaurantReplicaRepository
{
    public async Task<Restaurant?> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await context.Restaurants.SingleOrDefaultAsync(r => r.Id == id, cancellationToken);
    }

    public void Insert(Restaurant restaurant)
    {
        context.Restaurants.Add(restaurant);
    }
}

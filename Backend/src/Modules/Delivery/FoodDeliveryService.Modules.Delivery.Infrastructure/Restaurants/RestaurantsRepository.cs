using FoodDeliveryService.Modules.Delivery.Domain.Restaurants;
using FoodDeliveryService.Modules.Delivery.Infrastructure.Database;
using Microsoft.EntityFrameworkCore;

namespace FoodDeliveryService.Modules.Delivery.Infrastructure.Restaurants;

internal sealed class RestaurantsRepository(DeliveryDbContext context) : IRestaurantsRepository
{
    public async Task<Restaurant?> GetAsync(Guid restaurantId, CancellationToken cancellationToken = default)
    {
        return await context.Restaurants.SingleOrDefaultAsync(r => r.Id == restaurantId, cancellationToken);
    }

    public void Insert(Restaurant restaurant)
    {
        context.Restaurants.Add(restaurant);
    }
}

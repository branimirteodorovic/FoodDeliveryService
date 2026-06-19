using FoodDeliveryService.Modules.Restaurants.Domain.Restaurants;
using FoodDeliveryService.Modules.Restaurants.Infrastructure.Database;
using Microsoft.EntityFrameworkCore;

namespace FoodDeliveryService.Modules.Restaurants.Infrastructure.Restaurants;

internal sealed class RestaurantsRepository(RestaurantsDbContext context) : IRestaurantsRepository
{
    public async Task<Restaurant?> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await context.Restaurants.SingleOrDefaultAsync(c => c.Id == id, cancellationToken);
    }

    public void Insert(Restaurant restaurant)
    {
        context.Restaurants.Add(restaurant);
    }
}

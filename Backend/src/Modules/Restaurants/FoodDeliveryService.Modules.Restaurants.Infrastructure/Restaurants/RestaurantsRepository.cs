using FoodDeliveryService.Modules.Restaurants.Domain.Restaurants;
using FoodDeliveryService.Modules.Restaurants.Infrastructure.Database;
using Microsoft.EntityFrameworkCore;

namespace FoodDeliveryService.Modules.Restaurants.Infrastructure.Restaurants;

internal sealed class RestaurantsRepository(RestaurantsDbContext context) : IRestaurantsRepository
{
    public async Task<Restaurant?> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        // Full aggregate load — menu invariants (duplicate names, category existence) are enforced
        // by the root, so writes need categories + items in memory.
        return await context.Restaurants
            .Include(r => r.MenuCategories)
            .ThenInclude(c => c.Items)
            .SingleOrDefaultAsync(r => r.Id == id, cancellationToken);
    }

    public void Insert(Restaurant restaurant)
    {
        context.Restaurants.Add(restaurant);
    }
}

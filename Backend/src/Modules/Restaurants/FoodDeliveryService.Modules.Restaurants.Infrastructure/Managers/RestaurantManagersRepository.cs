using FoodDeliveryService.Modules.Restaurants.Domain.Managers;
using FoodDeliveryService.Modules.Restaurants.Infrastructure.Database;
using Microsoft.EntityFrameworkCore;

namespace FoodDeliveryService.Modules.Restaurants.Infrastructure.Managers;

internal sealed class RestaurantManagersRepository(RestaurantsDbContext context) : IRestaurantManagersRepository
{
    public async Task<RestaurantManager?> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await context.RestaurantManagers.SingleOrDefaultAsync(m => m.Id == id, cancellationToken);
    }

    public void Insert(RestaurantManager manager)
    {
        context.RestaurantManagers.Add(manager);
    }
}

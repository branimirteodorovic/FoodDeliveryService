using FoodDeliveryService.Modules.Orders.Domain.Restaurants;
using FoodDeliveryService.Modules.Orders.Infrastructure.Database;
using Microsoft.EntityFrameworkCore;

namespace FoodDeliveryService.Modules.Orders.Infrastructure.Restaurants;

internal sealed class MenuItemReplicaRepository(OrdersDbContext context) : IMenuItemReplicaRepository
{
    public async Task<MenuItem?> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await context.MenuItems.SingleOrDefaultAsync(i => i.Id == id, cancellationToken);
    }

    public async Task<IReadOnlyCollection<MenuItem>> GetManyAsync(
        Guid restaurantId,
        IReadOnlyCollection<Guid> menuItemIds,
        CancellationToken cancellationToken = default)
    {
        return await context.MenuItems
            .Where(i => i.RestaurantId == restaurantId && menuItemIds.Contains(i.Id))
            .ToListAsync(cancellationToken);
    }

    public void Insert(MenuItem menuItem)
    {
        context.MenuItems.Add(menuItem);
    }
}

using FoodDeliveryService.Modules.Orders.Domain.Orders;
using FoodDeliveryService.Modules.Orders.Infrastructure.Database;
using Microsoft.EntityFrameworkCore;

namespace FoodDeliveryService.Modules.Orders.Infrastructure.Orders;

internal sealed class OrdersRepository(OrdersDbContext context) : IOrdersRepository
{
    public async Task<Order?> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await context.Orders.SingleOrDefaultAsync(c => c.Id == id, cancellationToken);
    }

    public void Insert(Order order)
    {
        context.Orders.Add(order);
    }
}

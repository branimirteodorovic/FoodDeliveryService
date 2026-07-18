using FoodDeliveryService.Modules.Delivery.Domain.Orders;
using FoodDeliveryService.Modules.Delivery.Infrastructure.Database;
using Microsoft.EntityFrameworkCore;

namespace FoodDeliveryService.Modules.Delivery.Infrastructure.Orders;

internal sealed class OrdersRepository(DeliveryDbContext context) : IOrdersRepository
{
    public async Task<Order?> GetAsync(Guid orderId, CancellationToken cancellationToken = default)
    {
        return await context.Orders.SingleOrDefaultAsync(o => o.Id == orderId, cancellationToken);
    }

    public void Insert(Order order)
    {
        context.Orders.Add(order);
    }
}

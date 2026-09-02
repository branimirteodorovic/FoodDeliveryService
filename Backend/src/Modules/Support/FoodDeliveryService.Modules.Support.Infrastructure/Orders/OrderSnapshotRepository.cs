using FoodDeliveryService.Modules.Support.Domain.Orders;
using FoodDeliveryService.Modules.Support.Infrastructure.Database;
using Microsoft.EntityFrameworkCore;

namespace FoodDeliveryService.Modules.Support.Infrastructure.Orders;

internal sealed class OrderSnapshotRepository(SupportDbContext context) : IOrderSnapshotRepository
{
    public async Task<OrderSnapshot?> GetAsync(Guid orderId, CancellationToken cancellationToken = default)
    {
        return await context.OrderSnapshots.SingleOrDefaultAsync(o => o.Id == orderId, cancellationToken);
    }

    public void Insert(OrderSnapshot snapshot)
    {
        context.OrderSnapshots.Add(snapshot);
    }
}

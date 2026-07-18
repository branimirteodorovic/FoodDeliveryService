using FoodDeliveryService.Modules.Delivery.Domain.Deliveries;
using FoodDeliveryService.Modules.Delivery.Infrastructure.Database;
using Microsoft.EntityFrameworkCore;
using DeliveryAggregate = FoodDeliveryService.Modules.Delivery.Domain.Deliveries.Delivery;

namespace FoodDeliveryService.Modules.Delivery.Infrastructure.Deliveries;

internal sealed class DeliveriesRepository(DeliveryDbContext context) : IDeliveriesRepository
{
    public async Task<DeliveryAggregate?> GetAsync(Guid deliveryId, CancellationToken cancellationToken = default)
    {
        return await context.Deliveries.SingleOrDefaultAsync(d => d.Id == deliveryId, cancellationToken);
    }

    public async Task<DeliveryAggregate?> GetByOrderIdAsync(Guid orderId, CancellationToken cancellationToken = default)
    {
        return await context.Deliveries.SingleOrDefaultAsync(d => d.OrderId == orderId, cancellationToken);
    }

    public void Insert(DeliveryAggregate delivery)
    {
        context.Deliveries.Add(delivery);
    }
}

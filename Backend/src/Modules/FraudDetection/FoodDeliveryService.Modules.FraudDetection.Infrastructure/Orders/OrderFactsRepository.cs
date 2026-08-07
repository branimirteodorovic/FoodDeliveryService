using FoodDeliveryService.Modules.FraudDetection.Domain.Orders;
using FoodDeliveryService.Modules.FraudDetection.Infrastructure.Database;
using Microsoft.EntityFrameworkCore;

namespace FoodDeliveryService.Modules.FraudDetection.Infrastructure.Orders;

internal sealed class OrderFactsRepository(FraudDetectionDbContext context) : IOrderFactsRepository
{
    public async Task<OrderFact?> GetAsync(Guid orderId, CancellationToken cancellationToken = default)
    {
        return await context.OrderFacts.SingleOrDefaultAsync(o => o.Id == orderId, cancellationToken);
    }

    public void Insert(OrderFact orderFact)
    {
        context.OrderFacts.Add(orderFact);
    }
}

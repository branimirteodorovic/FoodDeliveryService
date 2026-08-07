namespace FoodDeliveryService.Modules.FraudDetection.Domain.Orders;

public interface IOrderFactsRepository
{
    Task<OrderFact?> GetAsync(Guid orderId, CancellationToken cancellationToken = default);

    void Insert(OrderFact orderFact);
}

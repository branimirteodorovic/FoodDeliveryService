namespace FoodDeliveryService.Modules.Delivery.Domain.Orders;

public interface IOrdersRepository
{
    Task<Order?> GetAsync(Guid orderId, CancellationToken cancellationToken = default);

    void Insert(Order order);
}

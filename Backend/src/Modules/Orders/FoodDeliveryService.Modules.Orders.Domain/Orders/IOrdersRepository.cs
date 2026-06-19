namespace FoodDeliveryService.Modules.Orders.Domain.Orders;

public interface IOrdersRepository
{
    Task<Order?> GetAsync(Guid id, CancellationToken cancellationToken = default);

    void Insert(Order order);
}

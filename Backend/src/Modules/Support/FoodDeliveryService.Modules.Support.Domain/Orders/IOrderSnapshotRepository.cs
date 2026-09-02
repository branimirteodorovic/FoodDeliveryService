namespace FoodDeliveryService.Modules.Support.Domain.Orders;

public interface IOrderSnapshotRepository
{
    Task<OrderSnapshot?> GetAsync(Guid orderId, CancellationToken cancellationToken = default);

    void Insert(OrderSnapshot snapshot);
}

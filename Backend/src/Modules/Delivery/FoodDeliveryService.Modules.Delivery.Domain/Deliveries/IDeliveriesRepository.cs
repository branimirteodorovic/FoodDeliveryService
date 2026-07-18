namespace FoodDeliveryService.Modules.Delivery.Domain.Deliveries;

public interface IDeliveriesRepository
{
    Task<Delivery?> GetAsync(Guid deliveryId, CancellationToken cancellationToken = default);

    Task<Delivery?> GetByOrderIdAsync(Guid orderId, CancellationToken cancellationToken = default);

    void Insert(Delivery delivery);
}

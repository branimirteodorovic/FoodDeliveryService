namespace FoodDeliveryService.Modules.Orders.Domain.Orders;

public interface IOrdersRepository
{
    Task<Order?> GetAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Idempotency lookup: a placement retry with a key that already produced an order must return
    /// that order instead of creating a second one.
    /// </summary>
    Task<Order?> GetByIdempotencyKeyAsync(string idempotencyKey, CancellationToken cancellationToken = default);

    void Insert(Order order);
}

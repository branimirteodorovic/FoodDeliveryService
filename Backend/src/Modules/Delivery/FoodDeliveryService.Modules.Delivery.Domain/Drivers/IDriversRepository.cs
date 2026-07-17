namespace FoodDeliveryService.Modules.Delivery.Domain.Drivers;

public interface IDriversRepository
{
    Task<Driver?> GetAsync(Guid driverId, CancellationToken cancellationToken = default);

    void Insert(Driver driver);
}

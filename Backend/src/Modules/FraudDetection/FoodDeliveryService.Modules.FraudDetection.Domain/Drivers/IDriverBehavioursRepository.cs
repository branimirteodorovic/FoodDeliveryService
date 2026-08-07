namespace FoodDeliveryService.Modules.FraudDetection.Domain.Drivers;

public interface IDriverBehavioursRepository
{
    Task<DriverBehaviour?> GetAsync(Guid driverId, CancellationToken cancellationToken = default);

    void Insert(DriverBehaviour driverBehaviour);
}

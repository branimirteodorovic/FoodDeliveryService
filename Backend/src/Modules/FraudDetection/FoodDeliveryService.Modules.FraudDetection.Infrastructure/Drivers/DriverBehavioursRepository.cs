using FoodDeliveryService.Modules.FraudDetection.Domain.Drivers;
using FoodDeliveryService.Modules.FraudDetection.Infrastructure.Database;
using Microsoft.EntityFrameworkCore;

namespace FoodDeliveryService.Modules.FraudDetection.Infrastructure.Drivers;

internal sealed class DriverBehavioursRepository(FraudDetectionDbContext context) : IDriverBehavioursRepository
{
    public async Task<DriverBehaviour?> GetAsync(Guid driverId, CancellationToken cancellationToken = default)
    {
        return await context.DriverBehaviours
            .SingleOrDefaultAsync(d => d.Id == driverId, cancellationToken);
    }

    public void Insert(DriverBehaviour driverBehaviour)
    {
        context.DriverBehaviours.Add(driverBehaviour);
    }
}

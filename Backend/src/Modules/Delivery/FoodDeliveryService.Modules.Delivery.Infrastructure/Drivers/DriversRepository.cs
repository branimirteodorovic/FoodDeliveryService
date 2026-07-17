using FoodDeliveryService.Modules.Delivery.Domain.Drivers;
using FoodDeliveryService.Modules.Delivery.Infrastructure.Database;
using Microsoft.EntityFrameworkCore;

namespace FoodDeliveryService.Modules.Delivery.Infrastructure.Drivers;

internal sealed class DriversRepository(DeliveryDbContext context) : IDriversRepository
{
    public async Task<Driver?> GetAsync(Guid driverId, CancellationToken cancellationToken = default)
    {
        return await context.Drivers.SingleOrDefaultAsync(d => d.Id == driverId, cancellationToken);
    }

    public void Insert(Driver driver)
    {
        context.Drivers.Add(driver);
    }
}

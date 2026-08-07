using FoodDeliveryService.Modules.FraudDetection.Domain.Customers;
using FoodDeliveryService.Modules.FraudDetection.Infrastructure.Database;
using Microsoft.EntityFrameworkCore;

namespace FoodDeliveryService.Modules.FraudDetection.Infrastructure.Customers;

internal sealed class CustomerBehavioursRepository(FraudDetectionDbContext context) : ICustomerBehavioursRepository
{
    public async Task<CustomerBehaviour?> GetAsync(Guid customerId, CancellationToken cancellationToken = default)
    {
        return await context.CustomerBehaviours
            .SingleOrDefaultAsync(c => c.Id == customerId, cancellationToken);
    }

    public void Insert(CustomerBehaviour customerBehaviour)
    {
        context.CustomerBehaviours.Add(customerBehaviour);
    }
}

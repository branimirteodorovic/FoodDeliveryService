using FoodDeliveryService.Modules.Orders.Domain.Customers;
using FoodDeliveryService.Modules.Orders.Infrastructure.Database;
using Microsoft.EntityFrameworkCore;

namespace FoodDeliveryService.Modules.Orders.Infrastructure.Customers;

internal sealed class CustomerPaymentProfileRepository(OrdersDbContext context)
    : ICustomerPaymentProfileRepository
{
    public async Task<CustomerPaymentProfile?> GetAsync(
        Guid customerId,
        CancellationToken cancellationToken = default)
    {
        return await context.CustomerPaymentProfiles
            .SingleOrDefaultAsync(p => p.Id == customerId, cancellationToken);
    }

    public void Insert(CustomerPaymentProfile profile)
    {
        context.CustomerPaymentProfiles.Add(profile);
    }
}

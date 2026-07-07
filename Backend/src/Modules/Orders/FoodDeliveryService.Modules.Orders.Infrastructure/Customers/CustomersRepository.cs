using FoodDeliveryService.Modules.Orders.Domain.Customers;
using FoodDeliveryService.Modules.Orders.Infrastructure.Database;
using Microsoft.EntityFrameworkCore;

namespace FoodDeliveryService.Modules.Orders.Infrastructure.Customers;

internal sealed class CustomersRepository(OrdersDbContext context) : ICustomerRepository
{
    public async Task<Customer?> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await context.Customers.SingleOrDefaultAsync(c => c.Id == id, cancellationToken);
    }

    public void Insert(Customer customer)
    {
        context.Customers.Add(customer);
    }
}

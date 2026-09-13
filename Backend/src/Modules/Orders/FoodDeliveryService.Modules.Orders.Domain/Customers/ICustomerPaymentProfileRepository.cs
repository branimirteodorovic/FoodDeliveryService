namespace FoodDeliveryService.Modules.Orders.Domain.Customers;

public interface ICustomerPaymentProfileRepository
{
    Task<CustomerPaymentProfile?> GetAsync(Guid customerId, CancellationToken cancellationToken = default);

    void Insert(CustomerPaymentProfile profile);
}

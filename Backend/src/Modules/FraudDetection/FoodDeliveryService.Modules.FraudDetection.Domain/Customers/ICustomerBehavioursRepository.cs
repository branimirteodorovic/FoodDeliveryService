namespace FoodDeliveryService.Modules.FraudDetection.Domain.Customers;

public interface ICustomerBehavioursRepository
{
    Task<CustomerBehaviour?> GetAsync(Guid customerId, CancellationToken cancellationToken = default);

    void Insert(CustomerBehaviour customerBehaviour);
}

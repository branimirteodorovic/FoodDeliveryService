namespace FoodDeliveryService.Modules.Payments.Domain.PaymentMethods;

public interface ICustomerPaymentProfileRepository
{
    Task<CustomerPaymentProfile?> GetAsync(Guid customerId, CancellationToken cancellationToken = default);

    void Insert(CustomerPaymentProfile profile);
}

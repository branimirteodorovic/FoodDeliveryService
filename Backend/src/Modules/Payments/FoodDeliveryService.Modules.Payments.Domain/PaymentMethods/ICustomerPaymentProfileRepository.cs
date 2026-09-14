namespace FoodDeliveryService.Modules.Payments.Domain.PaymentMethods;

public interface ICustomerPaymentProfileRepository
{
    Task<CustomerPaymentProfile?> GetAsync(Guid customerId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Resolves a profile from the provider's own customer identifier — the only link a webhook has
    /// back to a platform customer (§7), because a SetupIntent confirmed in a browser carries no
    /// identifier of ours. Backed by the unique index on <c>stripe_customer_id</c>.
    /// </summary>
    Task<CustomerPaymentProfile?> GetByStripeCustomerIdAsync(
        string stripeCustomerId,
        CancellationToken cancellationToken = default);

    void Insert(CustomerPaymentProfile profile);
}

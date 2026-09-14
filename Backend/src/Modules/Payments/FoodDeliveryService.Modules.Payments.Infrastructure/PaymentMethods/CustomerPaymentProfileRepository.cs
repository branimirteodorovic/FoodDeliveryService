using FoodDeliveryService.Modules.Payments.Domain.PaymentMethods;
using FoodDeliveryService.Modules.Payments.Infrastructure.Database;
using Microsoft.EntityFrameworkCore;

namespace FoodDeliveryService.Modules.Payments.Infrastructure.PaymentMethods;

internal sealed class CustomerPaymentProfileRepository(PaymentsDbContext context)
    : ICustomerPaymentProfileRepository
{
    public async Task<CustomerPaymentProfile?> GetAsync(
        Guid customerId,
        CancellationToken cancellationToken = default)
    {
        return await context.CustomerPaymentProfiles
            .SingleOrDefaultAsync(p => p.Id == customerId, cancellationToken);
    }

    public async Task<CustomerPaymentProfile?> GetByStripeCustomerIdAsync(
        string stripeCustomerId,
        CancellationToken cancellationToken = default)
    {
        return await context.CustomerPaymentProfiles
            .SingleOrDefaultAsync(p => p.StripeCustomerId == stripeCustomerId, cancellationToken);
    }

    public void Insert(CustomerPaymentProfile profile)
    {
        context.CustomerPaymentProfiles.Add(profile);
    }
}

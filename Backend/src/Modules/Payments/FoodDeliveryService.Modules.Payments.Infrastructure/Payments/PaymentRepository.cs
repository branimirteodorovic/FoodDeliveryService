using FoodDeliveryService.Modules.Payments.Domain.Payments;
using FoodDeliveryService.Modules.Payments.Infrastructure.Database;
using Microsoft.EntityFrameworkCore;

namespace FoodDeliveryService.Modules.Payments.Infrastructure.Payments;

internal sealed class PaymentRepository(PaymentsDbContext context) : IPaymentRepository
{
    public async Task<Payment?> GetByOrderIdAsync(Guid orderId, CancellationToken cancellationToken = default)
    {
        return await context.Payments
            .SingleOrDefaultAsync(p => p.OrderId == orderId, cancellationToken);
    }

    public async Task<Guid?> FindOrderIdByPaymentIntentIdAsync(
        string stripePaymentIntentId,
        CancellationToken cancellationToken = default)
    {
        // AsNoTracking and a projection, both deliberately: this read happens BEFORE the lock, only
        // to discover which lock to take. Tracking the entity here would hand the authoritative read
        // inside the lock the same pre-lock instance out of the identity map, and the lock would be
        // protecting a snapshot it had already let go stale.
        return await context.Payments
            .AsNoTracking()
            .Where(p => p.StripePaymentIntentId == stripePaymentIntentId)
            .Select(p => (Guid?)p.OrderId)
            .SingleOrDefaultAsync(cancellationToken);
    }

    public void Insert(Payment payment)
    {
        context.Payments.Add(payment);
    }
}

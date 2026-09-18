using FoodDeliveryService.Modules.Payments.Domain.Refunds;
using FoodDeliveryService.Modules.Payments.Infrastructure.Database;
using Microsoft.EntityFrameworkCore;

namespace FoodDeliveryService.Modules.Payments.Infrastructure.Refunds;

internal sealed class RefundRepository(PaymentsDbContext context) : IRefundRepository
{
    public async Task<Refund?> GetByRefundRequestIdAsync(
        Guid refundRequestId,
        CancellationToken cancellationToken = default)
    {
        return await context.Refunds
            .SingleOrDefaultAsync(r => r.RefundRequestId == refundRequestId, cancellationToken);
    }

    public async Task<decimal> GetSettledTotalForOrderAsync(
        Guid orderId,
        CancellationToken cancellationToken = default)
    {
        // Summed in the database rather than over loaded entities: this runs on the refund path of
        // every approved request, and the alternative loads every historical refund for the order to
        // add up one column.
        return await context.Refunds
            .Where(r => r.OrderId == orderId && r.Status == RefundStatus.Settled)
            .SumAsync(r => r.Amount.Amount, cancellationToken);
    }

    public void Insert(Refund refund)
    {
        context.Refunds.Add(refund);
    }
}

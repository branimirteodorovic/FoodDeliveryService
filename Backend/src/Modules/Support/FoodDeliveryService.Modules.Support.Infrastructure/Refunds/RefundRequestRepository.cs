using FoodDeliveryService.Modules.Support.Domain.Refunds;
using FoodDeliveryService.Modules.Support.Infrastructure.Database;
using Microsoft.EntityFrameworkCore;

namespace FoodDeliveryService.Modules.Support.Infrastructure.Refunds;

internal sealed class RefundRequestRepository(SupportDbContext context) : IRefundRequestRepository
{
    public async Task<RefundRequest?> GetAsync(
        Guid refundRequestId,
        CancellationToken cancellationToken = default)
    {
        return await context.RefundRequests
            .SingleOrDefaultAsync(r => r.Id == refundRequestId, cancellationToken);
    }

    public async Task<bool> HasActiveForOrderAsync(Guid orderId, CancellationToken cancellationToken = default)
    {
        // The same predicate the unique partial index carries. They are deliberately the same set:
        // if this read ever narrows further than the index, the handler starts letting through
        // exactly the rows the database will then refuse.
        return await context.RefundRequests.AnyAsync(
            r => r.OrderId == orderId &&
                 (r.Status == RefundStatus.Requested || r.Status == RefundStatus.Approved),
            cancellationToken);
    }

    public void Insert(RefundRequest refundRequest)
    {
        context.RefundRequests.Add(refundRequest);
    }
}

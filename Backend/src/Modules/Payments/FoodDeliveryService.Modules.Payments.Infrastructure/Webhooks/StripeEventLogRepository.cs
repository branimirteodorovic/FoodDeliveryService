using FoodDeliveryService.Modules.Payments.Domain.Webhooks;
using FoodDeliveryService.Modules.Payments.Infrastructure.Database;
using Microsoft.EntityFrameworkCore;

namespace FoodDeliveryService.Modules.Payments.Infrastructure.Webhooks;

internal sealed class StripeEventLogRepository(PaymentsDbContext context) : IStripeEventLogRepository
{
    public async Task<StripeEventLog?> GetAsync(Guid eventLogId, CancellationToken cancellationToken = default)
    {
        return await context.StripeEventLogs
            .SingleOrDefaultAsync(e => e.Id == eventLogId, cancellationToken);
    }

    public async Task<bool> ExistsAsync(string providerEventId, CancellationToken cancellationToken = default)
    {
        return await context.StripeEventLogs
            .AnyAsync(e => e.ProviderEventId == providerEventId, cancellationToken);
    }

    public void Insert(StripeEventLog eventLog)
    {
        context.StripeEventLogs.Add(eventLog);
    }
}

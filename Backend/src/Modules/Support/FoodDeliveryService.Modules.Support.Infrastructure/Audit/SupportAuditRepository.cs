using FoodDeliveryService.Modules.Support.Domain.Audit;
using FoodDeliveryService.Modules.Support.Infrastructure.Database;

namespace FoodDeliveryService.Modules.Support.Infrastructure.Audit;

// Insert only, mirroring the interface. Staging the entity is all this does: the caller's own
// SaveChangesAsync is what commits it, in the same transaction as the change it records.
internal sealed class SupportAuditRepository(SupportDbContext context) : ISupportAuditRepository
{
    public void Insert(SupportAuditEntry entry)
    {
        context.SupportAuditEntries.Add(entry);
    }
}

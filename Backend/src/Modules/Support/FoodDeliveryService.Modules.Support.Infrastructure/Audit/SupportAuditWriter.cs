using FoodDeliveryService.Common.Application.Clock;
using FoodDeliveryService.Modules.Support.Application.Abstractions.Audit;
using FoodDeliveryService.Modules.Support.Application.Abstractions.Authentication;
using FoodDeliveryService.Modules.Support.Domain.Audit;

namespace FoodDeliveryService.Modules.Support.Infrastructure.Audit;

// Thin by design: the value of this type is that there is exactly one of it. Every handler that
// changes a ticket calls Record and then the SaveChangesAsync it was already calling, so "the audit
// entry commits with the change" is structural rather than a rule each handler has to remember.
internal sealed class SupportAuditWriter(
    ISupportAuditRepository auditRepository,
    ISupportContext supportContext,
    IDateTimeProvider dateTimeProvider)
    : ISupportAuditWriter
{
    public void Record(
        Guid ticketId,
        SupportAuditAction action,
        string? fromValue = null,
        string? toValue = null,
        string? reason = null)
    {
        auditRepository.Insert(
            SupportAuditEntry.Create(
                ticketId,

                // From the token, never the body.
                supportContext.UserId,
                action,
                fromValue,
                toValue,
                reason,
                dateTimeProvider.UtcNow));
    }
}

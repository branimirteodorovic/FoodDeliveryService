using FoodDeliveryService.Modules.Support.Domain.Audit;

namespace FoodDeliveryService.Modules.Support.Application.Abstractions.Audit;

/// <summary>
/// The one way an audit entry is written. Command handlers call it immediately before
/// <c>IUnitOfWork.SaveChangesAsync</c>, so the entry and the state change it records commit or fail
/// together — a domain-event handler would run on the outbox's schedule instead, letting a
/// transition commit while its audit row failed independently.
/// <para>
/// Deliberately synchronous and void: it only stages the entity on the unit of work. The
/// <c>SaveChangesAsync</c> the handler was already going to call is what persists it, and there is
/// no second transaction to get wrong.
/// </para>
/// <para>
/// The actor and the timestamp are not parameters. Both are resolved here from the authenticated
/// caller and the clock, which is what makes them unforgeable by a request body.
/// </para>
/// </summary>
public interface ISupportAuditWriter
{
    void Record(
        Guid ticketId,
        SupportAuditAction action,
        string? fromValue = null,
        string? toValue = null,
        string? reason = null);
}

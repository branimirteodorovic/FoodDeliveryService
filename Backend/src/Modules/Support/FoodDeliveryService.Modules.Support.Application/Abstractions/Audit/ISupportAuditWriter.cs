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

    /// <summary>
    /// The same entry, attributed to the platform rather than to a person — Feature 3.8 Milestone H.
    /// <para>
    /// It exists because <see cref="Record"/> reads the actor off the authenticated caller, and the
    /// callers that need this one are integration-event handlers running in <c>ProcessInboxJob</c>
    /// where there is no HTTP context at all — <c>ISupportContext.UserId</c> throws there. A refund
    /// settling is still a fact about the money on a ticket and still belongs in the case history;
    /// what it does not have is an agent to blame for it.
    /// </para>
    /// <para>
    /// Deliberately a second method rather than a nullable actor parameter on the first. An audit
    /// writer that accepts an actor is one an agent's request body could eventually reach, which is
    /// precisely what <see cref="Record"/>'s missing parameter is there to prevent.
    /// </para>
    /// </summary>
    void RecordSystemAction(
        Guid ticketId,
        SupportAuditAction action,
        string? fromValue = null,
        string? toValue = null,
        string? reason = null);
}

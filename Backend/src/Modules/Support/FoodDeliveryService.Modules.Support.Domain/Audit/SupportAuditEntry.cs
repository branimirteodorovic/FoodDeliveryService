using FoodDeliveryService.Common.Domain;

namespace FoodDeliveryService.Modules.Support.Domain.Audit;

/// <summary>
/// One immutable record of one agent action on one ticket — who, when, what changed from what to
/// what, and why. This is the accountability surface of the whole Support service: it is what makes
/// "an agent refunded this order" a fact somebody can check rather than an assertion.
/// <para>
/// Append-only by construction. There is no update path, no delete path and no state to transition,
/// so unlike every other aggregate here it has no guarded methods and raises no domain events — the
/// entry <em>is</em> the record, and publishing an event about it would only invite a second,
/// divergent copy of the history.
/// </para>
/// <para>
/// Written in the same <c>SaveChangesAsync</c> as the change it describes, never from a
/// domain-event handler: the outbox runs on its own schedule, so a handler-written entry could fail
/// after its transition had already committed, which is exactly the hole this log exists to close.
/// <c>ISupportAuditWriter</c> is what keeps that from being re-derived in every command handler.
/// </para>
/// </summary>
public sealed class SupportAuditEntry : Entity
{
    public const int ValueMaxLength = 100;

    public const int ReasonMaxLength = 2000;

    private SupportAuditEntry()
    {
    }

    public Guid Id { get; private set; }

    public Guid TicketId { get; private set; }

    /// <summary>
    /// The agent or administrator who acted, taken from the authenticated caller. Never from a
    /// request body — an audit log whose actor an agent can name is worse than no audit log, because
    /// it looks like evidence.
    /// </summary>
    public Guid ActorId { get; private set; }

    public SupportAuditAction Action { get; private set; }

    /// <summary>The value before the change, as text. Null when the action has no "before".</summary>
    public string? FromValue { get; private set; }

    public string? ToValue { get; private set; }

    /// <summary>
    /// The reason the actor supplied. Internal to staff — this is why the audit endpoint is gated on
    /// <c>support-tickets:manage</c> and is never exposed to the customer.
    /// </summary>
    public string? Reason { get; private set; }

    public DateTime OccurredOnUtc { get; private set; }

    public static SupportAuditEntry Create(
        Guid ticketId,
        Guid actorId,
        SupportAuditAction action,
        string? fromValue,
        string? toValue,
        string? reason,
        DateTime utcNow)
    {
        return new SupportAuditEntry
        {
            Id = Guid.NewGuid(),
            TicketId = ticketId,
            ActorId = actorId,
            Action = action,
            FromValue = Truncate(fromValue, ValueMaxLength),
            ToValue = Truncate(toValue, ValueMaxLength),

            // Truncated rather than rejected: an over-long reason is a client-side annoyance, but a
            // failed audit write would roll back the state change it was recording, which turns a
            // cosmetic problem into a refused agent action.
            Reason = Truncate(reason, ReasonMaxLength),
            OccurredOnUtc = utcNow
        };
    }

    private static string? Truncate(string? value, int maxLength) =>
        value is null || value.Length <= maxLength ? value : value[..maxLength];
}

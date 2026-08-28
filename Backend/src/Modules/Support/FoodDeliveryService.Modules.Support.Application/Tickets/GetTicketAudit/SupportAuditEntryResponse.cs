using FoodDeliveryService.Modules.Support.Domain.Audit;

namespace FoodDeliveryService.Modules.Support.Application.Tickets.GetTicketAudit;

/// <summary>
/// One audit entry as the API returns it (hard rule #3 — the entity never leaves the module).
/// <para>
/// The actor's name comes from the local agent replica and is nullable on purpose: an entry must
/// survive the agent record being absent. The log's job is to be complete, so a missing name
/// degrades to a bare id rather than dropping the row from the history.
/// </para>
/// </summary>
public sealed record SupportAuditEntryResponse(
    Guid Id,
    Guid TicketId,
    Guid ActorId,
    string? ActorName,
    SupportAuditAction Action,
    string? FromValue,
    string? ToValue,
    string? Reason,
    DateTime OccurredOnUtc);

using FoodDeliveryService.Modules.Support.Domain.Tickets;

namespace FoodDeliveryService.Modules.Support.Application.Tickets.GetTicket;

/// <summary>
/// Response DTO for a single ticket (hard rule #3 — the aggregate never leaves the module).
///
/// EscalationTranscript is deliberately absent: it is a reserved column with no producer, and a
/// field that is always null on every response is noise the API contract does not need yet.
/// </summary>
public sealed record TicketResponse(
    Guid Id,
    string Reference,
    Guid CustomerId,
    Guid? OrderId,
    string Subject,
    TicketCategory Category,
    TicketPriority Priority,
    TicketStatus Status,
    TicketSource Source,
    Guid? AssignedAgentId,
    DateTime OpenedOnUtc,
    DateTime? FirstRespondedOnUtc,
    DateTime? ResolvedOnUtc,
    DateTime? ClosedOnUtc);

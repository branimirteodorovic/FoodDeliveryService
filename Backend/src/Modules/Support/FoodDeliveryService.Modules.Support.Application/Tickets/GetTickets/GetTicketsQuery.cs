using FoodDeliveryService.Common.Application.Messaging;

namespace FoodDeliveryService.Modules.Support.Application.Tickets.GetTickets;

/// <summary>
/// The agent queue, and a customer's own ticket list — the same query, narrowed in the handler.
///
/// Note what is NOT a parameter here: whose tickets these are. A customer's query is silently
/// scoped to their own customer id from the authenticated identity, so there is no filter value a
/// caller could send that widens what they see.
/// </summary>
public sealed record GetTicketsQuery(
    string? Status,
    string? Category,
    Guid? AssignedAgentId,
    bool Unassigned,
    DateTime? From,
    DateTime? To,
    int Page,
    int PageSize) : IQuery<IReadOnlyCollection<TicketSummaryResponse>>;

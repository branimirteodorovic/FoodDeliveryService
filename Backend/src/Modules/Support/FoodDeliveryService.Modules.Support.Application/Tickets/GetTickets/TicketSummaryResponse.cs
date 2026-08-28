using FoodDeliveryService.Modules.Support.Domain.Tickets;

namespace FoodDeliveryService.Modules.Support.Application.Tickets.GetTickets;

// The queue/list projection. The customer name and the order context are joined in from the
// replicas a later milestone adds; until they exist the ids are what the list carries.
public sealed record TicketSummaryResponse(
    Guid Id,
    string Reference,
    Guid CustomerId,
    Guid? OrderId,
    string Subject,
    TicketCategory Category,
    TicketPriority Priority,
    TicketStatus Status,
    Guid? AssignedAgentId,
    DateTime OpenedOnUtc,
    DateTime? ResolvedOnUtc);

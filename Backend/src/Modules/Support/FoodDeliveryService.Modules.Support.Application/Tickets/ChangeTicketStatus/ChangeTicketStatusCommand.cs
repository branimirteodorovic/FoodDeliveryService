using FoodDeliveryService.Common.Application.Messaging;

namespace FoodDeliveryService.Modules.Support.Application.Tickets.ChangeTicketStatus;

/// <summary>
/// One command for every agent-driven status move, dispatched to the matching aggregate method.
/// Five verb endpoints would each have to know which source states they are legal from, and the
/// aggregate already owns that table — this way there is exactly one copy of it.
///
/// <paramref name="Reason"/> carries whatever the target state needs: the resolution note for
/// Resolved, the escalation reason for Escalated. The aggregate decides whether it is required.
/// The acting agent is never in the body; it comes from the authenticated caller.
/// </summary>
public sealed record ChangeTicketStatusCommand(Guid TicketId, string Status, string? Reason) : ICommand;

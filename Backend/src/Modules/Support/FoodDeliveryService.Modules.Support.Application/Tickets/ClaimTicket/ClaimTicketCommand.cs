using FoodDeliveryService.Common.Application.Messaging;

namespace FoodDeliveryService.Modules.Support.Application.Tickets.ClaimTicket;

// The agent claiming the ticket is the authenticated caller — there is no agent id in this command,
// because taking a ticket for somebody else is an assignment (see AssignTicketCommand).
public sealed record ClaimTicketCommand(Guid TicketId) : ICommand;

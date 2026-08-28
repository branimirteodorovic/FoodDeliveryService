using FoodDeliveryService.Common.Application.Messaging;

namespace FoodDeliveryService.Modules.Support.Application.Tickets.UnassignTicket;

// The reason is not optional here as it is on assignment: a hand-back is the one assignment action
// whose motive cannot be read off its outcome. The aggregate enforces it, not only the validator.
public sealed record UnassignTicketCommand(Guid TicketId, string Reason) : ICommand;

using FoodDeliveryService.Common.Application.Messaging;

namespace FoodDeliveryService.Modules.Support.Application.Tickets.GetTicket;

// Ownership-scoped in the handler: an agent reads any ticket, a customer only their own, and
// another customer's ticket is a 404 rather than a 403.
public sealed record GetTicketQuery(Guid TicketId) : IQuery<TicketResponse>;

using FoodDeliveryService.Common.Application.Messaging;

namespace FoodDeliveryService.Modules.Support.Application.Tickets.OpenTicket;

/// <summary>
/// Opens a support ticket.
///
/// <paramref name="OnBehalfOfCustomerId"/> is the only way a caller can name somebody else, and it
/// is not the customer id — the customer id always comes from the authenticated caller. Supplying
/// it makes this an agent-created ticket (a phone call transcribed into the queue) and the handler
/// requires <c>support-tickets:manage</c> for it, so a customer cannot open a ticket in another
/// customer's name by adding one field to the body.
/// </summary>
public sealed record OpenTicketCommand(
    Guid? OnBehalfOfCustomerId,
    Guid? OrderId,
    string Subject,
    string Category) : ICommand<Guid>;

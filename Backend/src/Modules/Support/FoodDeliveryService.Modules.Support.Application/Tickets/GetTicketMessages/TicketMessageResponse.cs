using FoodDeliveryService.Modules.Support.Domain.Tickets;

namespace FoodDeliveryService.Modules.Support.Application.Tickets.GetTicketMessages;

/// <summary>
/// One message as the API returns it (hard rule #3 — the entity never leaves the module).
/// <para>
/// The author's name comes from the local agent replica and is null for a customer-authored
/// message: this module keeps no customer-name replica, and the customer reading their own thread
/// already knows who they are. <see cref="AuthorKind"/> is what a client renders the sides of the
/// conversation from.
/// </para>
/// </summary>
public sealed record TicketMessageResponse(
    Guid Id,
    Guid TicketId,
    Guid AuthorId,
    TicketAuthorKind AuthorKind,
    string? AuthorName,
    string Body,
    TicketMessageVisibility Visibility,
    DateTime PostedOnUtc);

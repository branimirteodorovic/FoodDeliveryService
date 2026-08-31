using FoodDeliveryService.Common.Application.Messaging;

namespace FoodDeliveryService.Modules.Support.Application.Tickets.GetTicketMessages;

/// <summary>
/// One ticket's thread, oldest first — a conversation reads forwards, unlike the audit log.
///
/// There is no "include internal notes" flag: what the caller sees is decided by their permissions
/// in the handler's SQL, so a client cannot ask for more than it is entitled to.
/// </summary>
public sealed record GetTicketMessagesQuery(Guid TicketId)
    : IQuery<IReadOnlyCollection<TicketMessageResponse>>;

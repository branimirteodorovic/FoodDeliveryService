using FoodDeliveryService.Common.Application.Messaging;

namespace FoodDeliveryService.Modules.Support.Application.Tickets.PostTicketMessage;

/// <summary>
/// Posts a message to a ticket's thread.
///
/// There is no author field and no author-kind field: both come from the authenticated caller.
/// <paramref name="Visibility"/> is the one thing the client chooses, and asking for
/// <c>InternalNote</c> without <c>support-tickets:manage</c> is refused rather than downgraded — a
/// note the author believes is internal and that the customer can read is the worst of the three
/// possible outcomes.
/// </summary>
public sealed record PostTicketMessageCommand(
    Guid TicketId,
    string Body,
    string Visibility) : ICommand<Guid>;

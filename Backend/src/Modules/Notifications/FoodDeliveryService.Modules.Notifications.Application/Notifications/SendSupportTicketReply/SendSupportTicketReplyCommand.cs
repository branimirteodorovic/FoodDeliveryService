using FoodDeliveryService.Common.Application.Messaging;

namespace FoodDeliveryService.Modules.Notifications.Application.Notifications.SendSupportTicketReply;

/// <summary>
/// Emails the customer that a support agent has replied on their ticket. Resolves the address from
/// the local RecipientUser replica — the same rule the order confirmation follows, and the reason
/// this module needs no new replica for the Support feature at all.
/// </summary>
/// <param name="Preview">
/// Already truncated by Support. Nothing here re-truncates it: one place decides how much of a
/// support conversation leaves that service, and it is the side that owns the conversation.
/// </param>
public sealed record SendSupportTicketReplyCommand(
    Guid CustomerId,
    string TicketReference,
    string TicketSubject,
    string Preview) : ICommand;

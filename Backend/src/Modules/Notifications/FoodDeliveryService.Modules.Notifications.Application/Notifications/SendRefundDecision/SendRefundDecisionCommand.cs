using FoodDeliveryService.Common.Application.Messaging;

namespace FoodDeliveryService.Modules.Notifications.Application.Notifications.SendRefundDecision;

/// <summary>
/// Emails the customer the outcome of a refund request their support agent raised. Resolves the
/// address from the local RecipientUser replica, like every other notification here — the Support
/// feature needs no new replica in this module.
/// </summary>
/// <param name="Approved">
/// Which of the two decision events this came from. One command for both outcomes because the two
/// emails are the same message with a different verdict, and a customer must receive one either
/// way: a request refused in silence is indistinguishable from one nobody read.
/// </param>
public sealed record SendRefundDecisionCommand(
    Guid CustomerId,
    string TicketReference,
    decimal Amount,
    bool Approved,
    string? DecisionNote) : ICommand;

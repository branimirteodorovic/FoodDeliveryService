using FoodDeliveryService.Common.Application.Messaging;

namespace FoodDeliveryService.Modules.Notifications.Application.Notifications.SendRefundSettled;

/// <summary>
/// Tells the customer the refund has actually been sent back to their card — Feature 3.8
/// Milestone H, §10.4.
/// <para>
/// The second of two emails about one refund, and deliberately not a replacement for the first.
/// <c>SendRefundDecisionCommand</c> answers "did anyone agree?" at the moment an administrator
/// decides; this answers "has the money gone?" whenever Payments gets it done, which can be a beat
/// later or — for a cash order, an uncaptured payment or a refused provider call — never. Folding
/// them into one message would mean either delaying the decision or promising the transfer before
/// it happened.
/// </para>
/// </summary>
public sealed record SendRefundSettledCommand(
    Guid CustomerId,
    string TicketReference,
    decimal Amount) : ICommand;

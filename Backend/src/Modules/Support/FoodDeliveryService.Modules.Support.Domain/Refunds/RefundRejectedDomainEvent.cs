using FoodDeliveryService.Common.Domain;

namespace FoodDeliveryService.Modules.Support.Domain.Refunds;

/// <summary>
/// An administrator declined a refund request. Deliberately the same shape as
/// <see cref="RefundApprovedDomainEvent"/> rather than a single event with an outcome field: the
/// two have different audiences downstream, and a consumer that cares about one must not have to
/// filter out the other and risk getting the predicate backwards.
/// </summary>
public sealed class RefundRejectedDomainEvent(
    Guid refundRequestId,
    Guid ticketId,
    string ticketReference,
    Guid orderId,
    Guid customerId,
    decimal amount,
    Guid requestedByAgentId,
    Guid decidedByAdminId,
    string? decisionNote,
    DateTime decidedOnUtc) : DomainEvent
{
    public Guid RefundRequestId { get; init; } = refundRequestId;

    public Guid TicketId { get; init; } = ticketId;

    /// <summary>The ticket's human-quotable reference — what the customer's email quotes.</summary>
    public string TicketReference { get; init; } = ticketReference;

    public Guid OrderId { get; init; } = orderId;

    public Guid CustomerId { get; init; } = customerId;

    public decimal Amount { get; init; } = amount;

    public Guid RequestedByAgentId { get; init; } = requestedByAgentId;

    public Guid DecidedByAdminId { get; init; } = decidedByAdminId;

    public string? DecisionNote { get; init; } = decisionNote;

    public DateTime DecidedOnUtc { get; init; } = decidedOnUtc;
}

using FoodDeliveryService.Common.Domain;

namespace FoodDeliveryService.Modules.Support.Domain.Refunds;

/// <summary>
/// An administrator approved a refund request.
/// <para>
/// Approved does not mean paid. Nothing in this platform moves money, and no service consumes the
/// integration event built from this — it emails the customer and nothing else. Carrying both the
/// requester and the decider is the point of the record: the two are never the same person, and the
/// event is what lets that be checked from outside Support.
/// </para>
/// </summary>
public sealed class RefundApprovedDomainEvent(
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

using FoodDeliveryService.Common.Application.EventBus;

namespace FoodDeliveryService.Modules.Support.IntegrationEvents;

/// <summary>
/// An administrator declined a refund request. A separate contract from
/// <see cref="RefundApprovedIntegrationEvent"/> rather than one event with an outcome field: a
/// consumer that cares about one outcome must not have to filter for it and risk getting the
/// predicate backwards — here that mistake would email a customer the opposite decision.
/// <para>
/// Nothing in Orders consumes this either; the customer is emailed, and that is all that happens.
/// </para>
/// </summary>
public sealed class RefundRejectedIntegrationEvent : IntegrationEvent
{
    public RefundRejectedIntegrationEvent(
        Guid id,
        DateTime occurredOnUtc,
        Guid refundRequestId,
        Guid ticketId,
        string ticketReference,
        Guid orderId,
        Guid customerId,
        decimal amount,
        Guid requestedByAgentId,
        Guid decidedByAdminId,
        string? decisionNote,
        DateTime decidedOnUtc)
        : base(id, occurredOnUtc)
    {
        RefundRequestId = refundRequestId;
        TicketId = ticketId;
        TicketReference = ticketReference;
        OrderId = orderId;
        CustomerId = customerId;
        Amount = amount;
        RequestedByAgentId = requestedByAgentId;
        DecidedByAdminId = decidedByAdminId;
        DecisionNote = decisionNote;
        DecidedOnUtc = decidedOnUtc;
    }

    public Guid RefundRequestId { get; init; }

    public Guid TicketId { get; init; }

    /// <summary>The ticket's human-quotable reference — what the customer's email quotes.</summary>
    public string TicketReference { get; init; }

    public Guid OrderId { get; init; }

    public Guid CustomerId { get; init; }

    public decimal Amount { get; init; }

    public Guid RequestedByAgentId { get; init; }

    public Guid DecidedByAdminId { get; init; }

    public string? DecisionNote { get; init; }

    public DateTime DecidedOnUtc { get; init; }
}

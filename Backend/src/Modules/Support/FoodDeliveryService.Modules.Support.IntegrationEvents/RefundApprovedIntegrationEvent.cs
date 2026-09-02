using FoodDeliveryService.Common.Application.EventBus;

namespace FoodDeliveryService.Modules.Support.IntegrationEvents;

/// <summary>
/// An administrator approved a refund request.
/// <para>
/// <strong>Approved does not mean paid, and Orders consumes nothing.</strong> The name invites the
/// opposite assumption, so it is worth stating on the contract itself: this platform processes no
/// payments by design. The only consumer today is Notifications, which emails the customer that the
/// decision was made. The refund record exists so that a real payment integration could later be
/// added <em>behind</em> this event without inventing the approval trail it would need.
/// </para>
/// <para>
/// Carries both <see cref="RequestedByAgentId"/> and <see cref="DecidedByAdminId"/> — a full
/// snapshot (hard rule #9), and the pair that makes segregation of duties checkable from outside
/// Support without asking it anything.
/// </para>
/// </summary>
public sealed class RefundApprovedIntegrationEvent : IntegrationEvent
{
    public RefundApprovedIntegrationEvent(
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

using FoodDeliveryService.Common.Application.EventBus;

namespace FoodDeliveryService.Modules.Support.IntegrationEvents;

/// <summary>
/// An administrator approved a refund request.
/// <para>
/// <strong>This event moves real money.</strong> It was published for two milestones with no
/// consumer but Notifications, and the contract said so: the platform processed no payments, and an
/// approval was an agreement. Feature 3.8 added the Payments service exactly where this comment said
/// one would go — <em>behind</em> this event, on top of the approval trail rather than in place of
/// it. Payments now refunds the captured card payment against it and answers with
/// <c>RefundSettled</c> or <c>RefundFailed</c>; Notifications is still the other consumer.
/// </para>
/// <para>
/// Approved still does not mean <em>paid</em>. The refund is asynchronous and can fail — a cash
/// order has nothing to refund, a payment that was never captured has nothing to give back — which
/// is why Support carries <c>Settled</c> and <c>Failed</c> statuses rather than treating approval
/// as the end of the story.
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

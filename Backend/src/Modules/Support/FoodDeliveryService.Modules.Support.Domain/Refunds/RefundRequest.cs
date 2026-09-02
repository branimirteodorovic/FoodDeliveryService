using FoodDeliveryService.Common.Domain;

namespace FoodDeliveryService.Modules.Support.Domain.Refunds;

/// <summary>
/// An agent's request that a customer be refunded for an order, and the administrator's decision on
/// it. Its own aggregate rather than a child of <c>Ticket</c>: it has a lifecycle the ticket does
/// not share (a ticket can be resolved while a refund is still awaiting a decision), and it is
/// contended for by a second actor whose authority is defined by <em>not</em> being the requester.
/// <para>
/// <strong>No money moves.</strong> This platform has no payment processing by design, and nothing
/// in Orders consumes the events this aggregate raises. What the record buys is the part a payment
/// integration cannot supply later: who asked, who agreed, for how much, and why. A real payment
/// integration would sit behind an approved request, not replace it.
/// </para>
/// <para>
/// Segregation of duties lives here, in <see cref="Approve"/> and <see cref="Reject"/>, not in the
/// permission set alone. <c>refunds:approve</c> being admin-only stops an agent from reaching the
/// endpoint at all; the check below stops the case a permission cannot see — an administrator who
/// also holds <c>refunds:request</c>, requests a refund, and then approves their own request.
/// </para>
/// </summary>
public sealed class RefundRequest : Entity
{
    public const int ReasonMaxLength = 1000;

    public const int DecisionNoteMaxLength = 1000;

    private RefundRequest()
    {
    }

    public Guid Id { get; private set; }

    /// <summary>The ticket the request was raised from — its audit trail is keyed on this.</summary>
    public Guid TicketId { get; private set; }

    /// <summary>
    /// The ticket's human-quotable reference (SUP-00001234), copied in at creation.
    /// <para>
    /// Denormalized deliberately. Every event this aggregate raises has to carry it — it is the
    /// identifier the customer sees in the decision email and quotes back — and a reference is
    /// immutable for the life of a ticket, so the usual objection to copying a field does not
    /// apply. The alternative is a read-back from the domain-event handler, which runs in the
    /// outbox job with no authenticated caller and therefore cannot use the ownership-scoped ticket
    /// query at all.
    /// </para>
    /// </summary>
    public string TicketReference { get; private set; }

    /// <summary>
    /// The order being refunded, taken from the ticket rather than from the request body: an agent
    /// must not be able to attach a refund to an order the case they are working never mentioned.
    /// </summary>
    public Guid OrderId { get; private set; }

    public Guid CustomerId { get; private set; }

    public decimal Amount { get; private set; }

    public string Reason { get; private set; }

    public RefundStatus Status { get; private set; }

    public Guid RequestedByAgentId { get; private set; }

    public Guid? DecidedByAdminId { get; private set; }

    public string? DecisionNote { get; private set; }

    public DateTime RequestedOnUtc { get; private set; }

    public DateTime? DecidedOnUtc { get; private set; }

    /// <param name="orderSubtotal">
    /// From the replicated <c>OrderSnapshot</c>, read by the handler and passed in. The aggregate
    /// does not reach for data — which is also what makes this rule testable without a database.
    /// </param>
    /// <param name="orderHasActiveRefundRequest">
    /// Whether this order already carries a requested or approved refund, likewise read by the
    /// handler. The unique partial index on the table is what actually wins the race; this check is
    /// what turns the ordinary case into a clean business failure rather than a constraint violation.
    /// </param>
    public static Result<RefundRequest> Create(
        Guid ticketId,
        string ticketReference,
        Guid? ticketOrderId,
        Guid customerId,
        decimal amount,
        decimal orderSubtotal,
        bool orderHasActiveRefundRequest,
        string reason,
        Guid requestedByAgentId,
        DateTime utcNow)
    {
        // A ticket with no order on it ("the app crashes at checkout") has nothing to refund, and
        // an order id supplied by the caller instead would make the subtotal ceiling meaningless.
        if (ticketOrderId is not { } orderId || orderId == Guid.Empty)
        {
            return Result.Failure<RefundRequest>(RefundErrors.TicketHasNoOrder);
        }

        if (amount <= 0)
        {
            return Result.Failure<RefundRequest>(RefundErrors.AmountNotPositive);
        }

        // The ceiling is the order's own subtotal. Refunding more than the customer paid is not a
        // refund, and the replica is the only place this service is allowed to learn that number.
        if (amount > orderSubtotal)
        {
            return Result.Failure<RefundRequest>(RefundErrors.AmountExceedsOrderSubtotal);
        }

        if (string.IsNullOrWhiteSpace(reason))
        {
            return Result.Failure<RefundRequest>(RefundErrors.ReasonRequired);
        }

        if (orderHasActiveRefundRequest)
        {
            return Result.Failure<RefundRequest>(RefundErrors.AlreadyRequestedForOrder);
        }

        var request = new RefundRequest
        {
            Id = Guid.NewGuid(),
            TicketId = ticketId,
            TicketReference = ticketReference,
            OrderId = orderId,
            CustomerId = customerId,
            Amount = amount,
            Reason = reason,
            Status = RefundStatus.Requested,
            RequestedByAgentId = requestedByAgentId,
            RequestedOnUtc = utcNow
        };

        request.Raise(new RefundRequestedDomainEvent(
            request.Id,
            request.TicketId,
            request.TicketReference,
            request.OrderId,
            request.CustomerId,
            request.Amount,
            request.Reason,
            request.RequestedByAgentId,
            request.RequestedOnUtc));

        return request;
    }

    public Result Approve(Guid adminId, string? note, DateTime utcNow)
    {
        Result decision = Decide(adminId, RefundStatus.Approved, note, utcNow);

        if (decision.IsFailure)
        {
            return decision;
        }

        Raise(new RefundApprovedDomainEvent(
            Id,
            TicketId,
            TicketReference,
            OrderId,
            CustomerId,
            Amount,
            RequestedByAgentId,
            adminId,
            note,
            utcNow));

        return Result.Success();
    }

    public Result Reject(Guid adminId, string? note, DateTime utcNow)
    {
        Result decision = Decide(adminId, RefundStatus.Rejected, note, utcNow);

        if (decision.IsFailure)
        {
            return decision;
        }

        Raise(new RefundRejectedDomainEvent(
            Id,
            TicketId,
            TicketReference,
            OrderId,
            CustomerId,
            Amount,
            RequestedByAgentId,
            adminId,
            note,
            utcNow));

        return Result.Success();
    }

    /// <summary>
    /// The half the two decisions share. Private and event-free, so there is exactly one place the
    /// two invariants — decided once, and never by the requester — are stated, and so a caller
    /// cannot reach a decision path that skips them.
    /// </summary>
    private Result Decide(Guid adminId, RefundStatus outcome, string? note, DateTime utcNow)
    {
        if (Status != RefundStatus.Requested)
        {
            return Result.Failure(RefundErrors.AlreadyDecided);
        }

        // Checked before any state is touched, so a refused self-approval raises no event and
        // leaves the request indistinguishable from one nobody has looked at yet.
        if (adminId == RequestedByAgentId)
        {
            return Result.Failure(RefundErrors.RequesterCannotDecide);
        }

        Status = outcome;
        DecidedByAdminId = adminId;
        DecisionNote = note;
        DecidedOnUtc = utcNow;

        return Result.Success();
    }
}

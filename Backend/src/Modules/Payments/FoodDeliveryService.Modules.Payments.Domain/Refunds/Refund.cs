using FoodDeliveryService.Common.Domain;

namespace FoodDeliveryService.Modules.Payments.Domain.Refunds;

/// <summary>
/// Money going back for one approved support refund — Feature 3.8 Milestone H, §10.
/// <para>
/// <b>This is the aggregate that makes Support's refund real.</b> Until this milestone an approved
/// refund was a record of an agreement and nothing moved; <c>SUPPORT_PHASE3_PLAN.md</c> said so
/// deliberately, and §10.3 retires that decision. What is retired is only the "nothing happens"
/// half — the approval trail Support built is still what authorizes this row to exist, and nothing
/// here can refund an order no administrator agreed to.
/// </para>
/// <para>
/// <b>One row per <see cref="RefundRequestId"/>, and that is the idempotency anchor.</b> The inbox
/// dispatches at least once, so the same approval can arrive twice; the unique index on that column
/// is what makes the second arrival find a row instead of refunding a second time, and
/// <c>PaymentIdempotencyKeys.Refund</c> — derived from the same id — is what stops the provider
/// acting twice if two deliveries somehow race past it.
/// </para>
/// <para>
/// <b>Keyed on the order, not on a payment id.</b> The order is the handle every writer in this
/// module already has (it is the lock key, §8.1), one order has at most one payment, and a refund
/// approved for a <em>cash</em> order has no payment to point at while still being worth recording.
/// A nullable payment id would be a second name for a row this one can already find.
/// </para>
/// </summary>
public sealed class Refund : Entity
{
    private Refund()
    {
    }

    public Guid Id { get; private set; }

    /// <summary>
    /// Support's <c>RefundRequest.Id</c>: what an administrator approved, the unique key of this
    /// table, and the durable id both the idempotency key and the settlement event are built from.
    /// </summary>
    public Guid RefundRequestId { get; private set; }

    /// <summary>The ticket the refund was raised on, carried so the settlement event is a full snapshot (hard rule #9).</summary>
    public Guid TicketId { get; private set; }

    /// <summary>
    /// The ticket's human-quotable reference (SUP-00001234). Denormalized for the same reason
    /// Support denormalizes it onto its own request: it is what the customer's email quotes, and
    /// this service may not ask Support for it (hard rule #5).
    /// </summary>
    public string TicketReference { get; private set; }

    public Guid OrderId { get; private set; }

    public Guid CustomerId { get; private set; }

    /// <summary>What is being given back — at or below the captured amount, checked by <c>Payment.Refund</c>.</summary>
    public Money Amount { get; private set; }

    public RefundStatus Status { get; private set; }

    /// <summary>Stripe's <c>re_…</c>, once the provider has accepted. Never leaves this service.</summary>
    public string? StripeRefundId { get; private set; }

    /// <summary>One of <see cref="RefundFailureReason"/>, or null. Never the provider's own text.</summary>
    public string? FailureReason { get; private set; }

    public DateTime CreatedOnUtc { get; private set; }

    public DateTime? SettledOnUtc { get; private set; }

    public DateTime? FailedOnUtc { get; private set; }

    /// <summary>
    /// Records the intent to refund, before anything is refunded — the same ordering as
    /// <c>Payment.Start</c>, and load-bearing for the same reason. Raises nothing: an approval
    /// Support already published is not news, and a refund that has not been attempted has no
    /// outcome to report.
    /// </summary>
    public static Result<Refund> Start(
        Guid refundId,
        Guid refundRequestId,
        Guid ticketId,
        string ticketReference,
        Guid orderId,
        Guid customerId,
        Money amount,
        DateTime utcNow)
    {
        ArgumentNullException.ThrowIfNull(amount);

        if (amount.Amount <= 0m)
        {
            return Result.Failure<Refund>(RefundErrors.AmountNotPositive);
        }

        if (string.IsNullOrWhiteSpace(ticketReference))
        {
            return Result.Failure<Refund>(RefundErrors.TicketReferenceRequired);
        }

        return new Refund
        {
            Id = refundId,
            RefundRequestId = refundRequestId,
            TicketId = ticketId,
            TicketReference = ticketReference,
            OrderId = orderId,
            CustomerId = customerId,
            Amount = amount,
            Status = RefundStatus.Pending,
            CreatedOnUtc = utcNow
        };
    }

    /// <summary>
    /// The provider accepted the refund.
    /// <para>
    /// <b>A refund Stripe reports as <c>pending</c> settles here too</b>, and that is a decision
    /// rather than an oversight. A card refund is frequently accepted and not yet cleared, the money
    /// is already committed to going back at that point, and this platform subscribes to no
    /// <c>charge.refund.updated</c> webhook that would ever move it on. Holding it in
    /// <see cref="RefundStatus.Pending"/> would leave Support's request stuck mid-air for a refund
    /// that is going to arrive — a worse answer than the small one given by settling a few days
    /// early. A refund later reversed by the issuer is outside what this feature models at all.
    /// </para>
    /// <para>
    /// A second call is absorbed, so a redelivered approval never publishes a second settlement.
    /// </para>
    /// </summary>
    public Result Settle(string stripeRefundId, DateTime utcNow)
    {
        if (string.IsNullOrWhiteSpace(stripeRefundId))
        {
            return Result.Failure(RefundErrors.ProviderRefundRequired);
        }

        if (Status != RefundStatus.Pending)
        {
            return Result.Success();
        }

        Status = RefundStatus.Settled;
        StripeRefundId = stripeRefundId;
        SettledOnUtc = utcNow;

        Raise(new RefundSettledDomainEvent(
            Id,
            RefundRequestId,
            TicketId,
            TicketReference,
            OrderId,
            CustomerId,
            Amount.Amount,
            Amount.Currency,
            utcNow));

        return Result.Success();
    }

    /// <summary>
    /// No money went back. Reached from the three guards that refuse before a call is made — a cash
    /// order, an uncaptured payment, an amount above what was taken — and from a provider refusal.
    /// <para>
    /// <b>Every one of those refusals is published rather than logged.</b> Support is waiting on an
    /// answer it can show an agent, and a refund that silently does not happen is the failure mode
    /// this milestone exists to remove, not one it may introduce at the other end.
    /// </para>
    /// </summary>
    public Result Fail(string reason, DateTime utcNow)
    {
        if (!RefundFailureReason.IsKnown(reason))
        {
            // Bounded here rather than trusted: the value reaches a metric tag and Support's audit
            // log, and the caller maps a third party's vocabulary onto it.
            return Result.Failure(RefundErrors.FailureReasonUnknown(reason));
        }

        if (Status != RefundStatus.Pending)
        {
            return Result.Success();
        }

        Status = RefundStatus.Failed;
        FailureReason = reason;
        FailedOnUtc = utcNow;

        Raise(new RefundFailedDomainEvent(
            Id,
            RefundRequestId,
            TicketId,
            TicketReference,
            OrderId,
            CustomerId,
            Amount.Amount,
            Amount.Currency,
            reason,
            utcNow));

        return Result.Success();
    }
}

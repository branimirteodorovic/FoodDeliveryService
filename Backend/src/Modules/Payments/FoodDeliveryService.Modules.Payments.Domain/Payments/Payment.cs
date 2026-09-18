using FoodDeliveryService.Common.Domain;

namespace FoodDeliveryService.Modules.Payments.Domain.Payments;

/// <summary>
/// One order's money, from the hold to whatever became of it — Feature 3.8 Milestone F, §8.1.
/// <para>
/// <b>It exists before the provider is called, and that ordering is load-bearing.</b> The row is
/// written in <see cref="PaymentStatus.Authorizing"/> and only then does anything talk to Stripe, so
/// a crash, a timeout or a lost response leaves a payment the reconciling webhook (§7) can find and
/// finish. The other order — call first, record the answer — has a failure mode with no recovery at
/// all: an authorization held at the provider that this platform has no row for, no id for and no
/// way to look up.
/// </para>
/// <para>
/// <b>Every transition here is driven by two processes at once</b> — the outbox handler acting on
/// <c>OrderPlacedIntegrationEvent</c> and the webhook arm acting on <c>payment_intent.*</c> — and
/// neither is ordered with respect to the other (§7.5). So each one is idempotent on its own, the
/// terminal states are no-ops (§8.6), and the handlers take <c>IDistributedLock</c> before their
/// read: no aggregate in this codebase carries an optimistic concurrency token, so Postgres will not
/// reject the second write (§1.4, rule 2).
/// </para>
/// <para>
/// <b>Cash orders never reach here.</b> No <see cref="Payment"/> row is created for them — there is
/// nothing to authorize, and a row claiming otherwise would put every cash order into a state
/// machine that has no way to leave it.
/// </para>
/// </summary>
public sealed class Payment : Entity
{
    private Payment()
    {
    }

    public Guid Id { get; private set; }

    /// <summary>The order being paid for. Unique across the table — one order, one payment.</summary>
    public Guid OrderId { get; private set; }

    /// <summary>The Users service's UserId, so a payment can be answered for without joining to Orders.</summary>
    public Guid CustomerId { get; private set; }

    /// <summary>
    /// What is being charged: <c>Order.Subtotal</c> in the platform currency (§0.4). Snapshotted
    /// here rather than read back from the order, because the amount authorized must stay the
    /// amount that was authorized even if the order is later corrected.
    /// </summary>
    public Money Amount { get; private set; }

    public PaymentStatus Status { get; private set; }

    /// <summary>
    /// Stripe's <c>pi_…</c>, once there is one. Null for the moment between the row being written
    /// and the provider answering, and for a payment that failed before a call was made at all.
    /// Never leaves this service.
    /// </summary>
    public string? StripePaymentIntentId { get; private set; }

    /// <summary>One of <see cref="PaymentFailureReason"/>, or null. Never the provider's own text.</summary>
    public string? FailureReason { get; private set; }

    public DateTime CreatedOnUtc { get; private set; }

    public DateTime? AuthorizedOnUtc { get; private set; }

    public DateTime? FailedOnUtc { get; private set; }

    public DateTime? CapturedOnUtc { get; private set; }

    public DateTime? ReleasedOnUtc { get; private set; }

    /// <summary>
    /// When the last of the captured money went back — Feature 3.8 Milestone H. Null while any of it
    /// is still held by the business, including for a payment that has been partly refunded: the
    /// per-refund timestamps live on the <c>refunds</c> rows, and this one answers the single
    /// question the payment itself is asked, which is whether the customer still owes anything.
    /// </summary>
    public DateTime? RefundedOnUtc { get; private set; }

    /// <summary>
    /// Nothing more will happen to this money without a new decision by a person — §8.6. Every
    /// mutation below returns success without acting once this is true, which is what makes a
    /// redelivered webhook or a second outbox dispatch harmless rather than a second charge.
    /// </summary>
    public bool IsTerminal => Status
        is PaymentStatus.Captured
        or PaymentStatus.Released
        or PaymentStatus.Failed
        or PaymentStatus.Refunded;

    /// <summary>
    /// Records the intent to charge, before anything is charged. Raises nothing: an authorization
    /// that has not been attempted is not news to any other service, and Orders already wrote its
    /// own <c>PaymentStatus.Authorizing</c> at placement.
    /// </summary>
    public static Result<Payment> Start(
        Guid paymentId,
        Guid orderId,
        Guid customerId,
        Money amount,
        DateTime utcNow)
    {
        ArgumentNullException.ThrowIfNull(amount);

        // Money.Create allows zero — a running refund total starts there. A charge does not: an
        // authorization for nothing is a provider call that fails in a way nobody can act on.
        if (amount.Amount <= 0m)
        {
            return Result.Failure<Payment>(PaymentErrors.AmountNotPositive);
        }

        return new Payment
        {
            Id = paymentId,
            OrderId = orderId,
            CustomerId = customerId,
            Amount = amount,
            Status = PaymentStatus.Authorizing,
            CreatedOnUtc = utcNow
        };
    }

    /// <summary>
    /// The funds are held. Called from the authorizing handler with the intent Stripe just returned,
    /// and from the <c>payment_intent.amount_capturable_updated</c> webhook arm with the intent
    /// Stripe is telling us about — whichever gets there first (§7.5).
    /// <para>
    /// A second call with the same intent is a no-op that raises nothing, so no consumer re-projects
    /// a change that did not happen. A call naming a <em>different</em> intent is refused: two
    /// intents for one order means two holds on one card, and silently overwriting the id would lose
    /// the one that is still capturable.
    /// </para>
    /// </summary>
    public Result Authorize(string stripePaymentIntentId, DateTime utcNow)
    {
        if (string.IsNullOrWhiteSpace(stripePaymentIntentId))
        {
            return Result.Failure(PaymentErrors.PaymentIntentRequired);
        }

        if (StripePaymentIntentId is not null &&
            !string.Equals(StripePaymentIntentId, stripePaymentIntentId, StringComparison.Ordinal))
        {
            return Result.Failure(PaymentErrors.PaymentIntentMismatch(OrderId));
        }

        // Terminal first, so a webhook that arrives after a capture, a release or a failure is
        // absorbed rather than resurrecting the payment (§8.6).
        if (IsTerminal || Status == PaymentStatus.Authorized)
        {
            return Result.Success();
        }

        Status = PaymentStatus.Authorized;
        StripePaymentIntentId = stripePaymentIntentId;
        AuthorizedOnUtc = utcNow;
        FailureReason = null;

        Raise(new PaymentAuthorizedDomainEvent(
            Id,
            OrderId,
            CustomerId,
            Amount.Amount,
            Amount.Currency,
            utcNow));

        return Result.Success();
    }

    /// <summary>
    /// The held funds were taken — Feature 3.8 Milestone G, §9. Driven by the restaurant accepting
    /// the order, and reconciled by the <c>payment_intent.succeeded</c> webhook arm when that call's
    /// response never came back.
    /// <para>
    /// <b>Only from <see cref="PaymentStatus.Authorized"/>, and silently.</b> The same shape as
    /// <see cref="Fail"/> from <see cref="PaymentStatus.Authorizing"/>: anything else is a second
    /// delivery of a message this platform has already acted on, and a second capture is a second
    /// charge. A payment still <see cref="PaymentStatus.Authorizing"/> cannot be captured either —
    /// there is no hold to take yet, and the acceptance that drove this could only have happened
    /// because Orders projected an authorization that this row has not caught up with.
    /// </para>
    /// </summary>
    public Result Capture(DateTime utcNow)
    {
        if (Status != PaymentStatus.Authorized)
        {
            return Result.Success();
        }

        Status = PaymentStatus.Captured;
        CapturedOnUtc = utcNow;

        Raise(new PaymentCapturedDomainEvent(
            Id,
            OrderId,
            CustomerId,
            Amount.Amount,
            Amount.Currency,
            utcNow));

        return Result.Success();
    }

    /// <summary>
    /// The hold was given up without ever being charged — Feature 3.8 Milestone G, §9. Driven by the
    /// restaurant rejecting the order or the customer cancelling it.
    /// <para>
    /// The mirror of <see cref="Capture"/>, and a no-op from anything but
    /// <see cref="PaymentStatus.Authorized"/> for the same reasons — with one asymmetry worth
    /// knowing: a cancellation that arrives while the payment is still
    /// <see cref="PaymentStatus.Authorizing"/> is absorbed here, and the hold that the in-flight
    /// authorization is about to place is then released by nothing. It expires at the issuer instead,
    /// within a week. That window and the two ways to close it are written up in §9.1 of the plan;
    /// it is left open here deliberately, because the alternatives both contradict a decision
    /// Milestone F took on purpose.
    /// </para>
    /// </summary>
    public Result Release(DateTime utcNow)
    {
        if (Status != PaymentStatus.Authorized)
        {
            return Result.Success();
        }

        Status = PaymentStatus.Released;
        ReleasedOnUtc = utcNow;

        Raise(new PaymentReleasedDomainEvent(
            Id,
            OrderId,
            CustomerId,
            Amount.Amount,
            Amount.Currency,
            utcNow));

        return Result.Success();
    }

    /// <summary>
    /// Money that was taken is going back — Feature 3.8 Milestone H, §10.1. Called once the provider
    /// has accepted a refund for an approved support request.
    /// <para>
    /// <b>The one mutation that is not a no-op from a terminal state</b>, because the state it works
    /// from — <see cref="PaymentStatus.Captured"/> — is itself terminal. The terminal rule exists to
    /// stop a redelivered message repeating an action; a refund is not a repeat of the capture, it
    /// is the only thing that may legitimately follow one. So this one guards on the status it needs
    /// instead, and the <c>Refund</c> aggregate carries the idempotency that the terminal
    /// rule carries elsewhere: one row per approved request, so a second delivery finds the refund
    /// already settled and never reaches here.
    /// </para>
    /// <para>
    /// <b>This is the authoritative ceiling</b>, and the second of two. Support caps the
    /// <em>request</em> at the replicated order subtotal when an agent raises it, which is a check
    /// against what the customer was asked to pay; this is a check against what was actually taken
    /// minus what has already gone back, which is the number that can be exceeded. They are not the
    /// same number — a capture that never happened makes the first one pass and this one refuse.
    /// </para>
    /// <para>
    /// Raises nothing. The refund is its own aggregate and the event belongs to it; this method
    /// moves the payment's own status once the last of the money is back, and a payment that is
    /// partly refunded stays <see cref="PaymentStatus.Captured"/> so a further refund is still
    /// possible.
    /// </para>
    /// </summary>
    /// <param name="refundAmount">What is going back now.</param>
    /// <param name="alreadyRefunded">
    /// The settled total for this payment so far, in major units, read by the handler and passed in
    /// — the aggregate does not reach for data, which is also what makes this rule testable without
    /// a database. Both this read and the write below happen under <c>PaymentLocks.Payment</c>, so
    /// two refunds cannot each see the other's allowance as unspent.
    /// </param>
    public Result Refund(Money refundAmount, decimal alreadyRefunded, DateTime utcNow)
    {
        Result refundable = EnsureRefundable(refundAmount, alreadyRefunded);

        if (refundable.IsFailure)
        {
            return refundable;
        }

        // Only when the last cent is back. A partial refund leaves the payment Captured, which is
        // what keeps the door open for the next one — the status is about the money, not about how
        // many refunds have touched it.
        if (alreadyRefunded + refundAmount.Amount == Amount.Amount)
        {
            Status = PaymentStatus.Refunded;
            RefundedOnUtc = utcNow;
        }

        return Result.Success();
    }

    /// <summary>
    /// The same rules as <see cref="Refund"/>, asked rather than applied.
    /// <para>
    /// It exists because of an ordering this aggregate cannot impose on its own: the provider call
    /// sits between the decision and the state change, and a payment moved to
    /// <see cref="PaymentStatus.Refunded"/> before Stripe agreed would be a record of money that may
    /// never have gone anywhere. So the handler asks first, calls the provider, and applies after —
    /// and the conditions are stated once, here, rather than copied into the question and the
    /// answer.
    /// </para>
    /// </summary>
    public Result EnsureRefundable(Money refundAmount, decimal alreadyRefunded)
    {
        ArgumentNullException.ThrowIfNull(refundAmount);

        if (Status is not (PaymentStatus.Captured or PaymentStatus.Refunded))
        {
            return Result.Failure(PaymentErrors.NotCaptured(OrderId));
        }

        if (refundAmount.Amount <= 0m)
        {
            return Result.Failure(PaymentErrors.AmountNotPositive);
        }

        if (alreadyRefunded + refundAmount.Amount > Amount.Amount)
        {
            return Result.Failure(PaymentErrors.RefundExceedsCaptured(OrderId));
        }

        return Result.Success();
    }

    /// <summary>
    /// The hold never happened. Reached from a decline, an off-session 3-D Secure challenge, a
    /// customer with no saved card, a provider refusal, and the
    /// <c>payment_intent.payment_failed</c> webhook arm.
    /// <para>
    /// Only from <see cref="PaymentStatus.Authorizing"/>. A failure event about a payment that is
    /// already authorized, captured, released or failed is late news about an earlier attempt, and
    /// is absorbed: turning an authorized payment into a failed one would cancel an order whose
    /// money is sitting on a card.
    /// </para>
    /// <para>
    /// <paramref name="stripePaymentIntentId"/> is recorded when the failure came back from an
    /// intent that was actually created — a declined off-session charge leaves one behind, and it is
    /// what an operator quotes in the Stripe dashboard. Null when the call never went out.
    /// </para>
    /// </summary>
    public Result Fail(string reason, string? stripePaymentIntentId, DateTime utcNow)
    {
        if (!PaymentFailureReason.IsKnown(reason))
        {
            // The bound is enforced here rather than trusted, because the caller is a mapping from a
            // third party's vocabulary and this value reaches a metric tag (§11.1).
            return Result.Failure(PaymentErrors.FailureReasonUnknown(reason));
        }

        if (Status != PaymentStatus.Authorizing)
        {
            return Result.Success();
        }

        Status = PaymentStatus.Failed;
        FailureReason = reason;
        FailedOnUtc = utcNow;

        if (stripePaymentIntentId is not null)
        {
            StripePaymentIntentId = stripePaymentIntentId;
        }

        Raise(new PaymentAuthorizationFailedDomainEvent(Id, OrderId, CustomerId, reason, utcNow));

        return Result.Success();
    }
}

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

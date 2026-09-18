using FoodDeliveryService.Common.Domain;

namespace FoodDeliveryService.Modules.Payments.Domain.Payments;

/// <summary>
/// Errors the payment gateway seam produces, and the <see cref="Payment"/> aggregate's own. The
/// refund errors (refund exceeds the captured amount) join them in §10.
/// </summary>
public static class PaymentErrors
{
    /// <summary>
    /// The card was refused. A business outcome, not a fault: retrying the same call produces the
    /// same refusal, so nothing above this should treat it as transient.
    /// </summary>
    public static PaymentDeclinedError Declined(string reason) => new(
        reason,
        $"The payment was declined by the card issuer ({reason})");

    /// <summary>
    /// Stripe rejected the request itself — a parameter we sent is wrong, or an idempotency key was
    /// reused with different parameters. <see cref="ErrorType.Failure"/>, so it surfaces as a 500
    /// rather than a 400: the caller did nothing wrong and there is nothing they can change.
    /// </summary>
    public static Error GatewayRequestInvalid(string code) => Error.Failure(
        "Payments.GatewayRequestInvalid",
        $"The payment provider rejected the request ({code})");

    /// <summary>
    /// Stripe rejected the API key. Almost always a missing or stale <c>Stripe:SecretKey</c> — the
    /// symptom of forgetting the user-secret in local development, where the configuration
    /// fail-fast in <c>Program.cs</c> deliberately does not run.
    /// </summary>
    public static readonly Error GatewayNotAuthenticated = Error.Failure(
        "Payments.GatewayNotAuthenticated",
        "The payment provider rejected this service's API key");

    /// <summary>
    /// Another process holds this payment's lock — Feature 3.8 Milestone F, §8.1.
    /// <para>
    /// <b>A failure, not a success.</b> A lost acquisition must land somewhere a retry exists, and
    /// on this path there is none: <c>ProcessInboxJob</c> and <c>ProcessOutboxJob</c> both record
    /// the error and mark the message processed (§5.6). Returning success would strand the payment
    /// silently; returning this leaves the reason on the message row, which is what an operator
    /// reads and what the reconciling webhook (§7) exists to finish.
    /// </para>
    /// </summary>
    public static Error AuthorizationInProgress(Guid orderId) => Error.Conflict(
        "Payments.AuthorizationInProgress",
        $"Another process is currently changing the payment for order {orderId}");

    /// <summary>
    /// No <see cref="Payment"/> row for the order or the provider intent the caller named. On the
    /// webhook side it means a <c>payment_intent.*</c> arrived for an intent this platform never
    /// created — a different environment pointed at the same webhook endpoint, most likely.
    /// </summary>
    public static Error NotFound(string reference) => Error.NotFound(
        "Payments.NotFound",
        $"No payment was found for {reference}");

    /// <summary>
    /// The customer has no payment profile at all, so there is no Stripe customer to charge. Means
    /// <c>UserRegisteredIntegrationEvent</c> has not been consumed for them yet (§6.2).
    /// </summary>
    public static Error CustomerProfileNotFound(Guid customerId) => Error.NotFound(
        "Payments.CustomerProfileNotFound",
        $"The customer {customerId} has no payment profile");

    /// <summary>
    /// A charge must be strictly positive. <c>Money</c> itself allows zero — a running refund total
    /// starts there — so the rule belongs to the aggregate that spends the money, not to the type.
    /// </summary>
    public static readonly Error AmountNotPositive = Error.Problem(
        "Payments.AmountNotPositive",
        "A payment amount must be greater than zero");

    /// <summary>
    /// The capture call came back without the provider agreeing the money moved — Feature 3.8
    /// Milestone G. A manual-capture intent that has been captured is <c>succeeded</c>; anything
    /// else means the platform must not record a charge, because Orders, Support and §10's refund
    /// ceiling would all then be reasoning about money that may still be sitting on a hold.
    /// </summary>
    public static Error CaptureNotConfirmed(Guid orderId) => Error.Failure(
        "Payments.CaptureNotConfirmed",
        $"The payment provider did not confirm the capture for order {orderId}");

    /// <summary>The release call's mirror of <see cref="CaptureNotConfirmed"/>: a cancelled intent is <c>canceled</c>.</summary>
    public static Error ReleaseNotConfirmed(Guid orderId) => Error.Failure(
        "Payments.ReleaseNotConfirmed",
        $"The payment provider did not confirm the release for order {orderId}");

    /// <summary>
    /// A refund was asked for against money that was never taken — Feature 3.8 Milestone H, §10.1.
    /// The hold was released, the authorization failed, or it is still held and the order has not
    /// been accepted. Releasing a hold is not a refund, and there is nothing to give back.
    /// <para>
    /// Not a handler failure: the refund handler turns it into a recorded, published
    /// <c>RefundFailureReason.PaymentNotCaptured</c>, because the person waiting on the answer is
    /// the agent who raised the request.
    /// </para>
    /// </summary>
    public static Error NotCaptured(Guid orderId) => Error.Problem(
        "Payments.NotCaptured",
        $"The payment for order {orderId} has not been captured, so there is nothing to refund");

    /// <summary>
    /// More was asked for than is left — the authoritative half of §10.1's ceiling. Support's own
    /// cap is against the replicated order subtotal at the time the request was raised; this one is
    /// against what was actually captured minus what has already gone back, and only this one can
    /// see a second refund on the same order.
    /// </summary>
    public static Error RefundExceedsCaptured(Guid orderId) => Error.Problem(
        "Payments.RefundExceedsCaptured",
        $"The refund exceeds what is left of the captured amount for order {orderId}");

    public static readonly Error PaymentIntentRequired = Error.Problem(
        "Payments.PaymentIntentRequired",
        "A provider payment intent identifier is required");

    /// <summary>
    /// A second, different <c>pi_…</c> for one order. Two intents means two holds on one card, and
    /// overwriting the identifier would lose the one that is still capturable — so this is refused
    /// loudly rather than reconciled by guesswork.
    /// </summary>
    public static Error PaymentIntentMismatch(Guid orderId) => Error.Conflict(
        "Payments.PaymentIntentMismatch",
        $"The payment for order {orderId} is already held against a different provider intent");

    /// <summary>
    /// A reason outside <see cref="PaymentFailureReason"/> reached the aggregate. Checked rather
    /// than trusted because the value comes from a mapping of a third party's vocabulary and ends up
    /// on a metric tag (§8.4).
    /// </summary>
    public static Error FailureReasonUnknown(string reason) => Error.Problem(
        "Payments.FailureReasonUnknown",
        $"'{reason}' is not a recognised payment failure reason");
}

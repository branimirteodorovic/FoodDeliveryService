using System.Collections.Frozen;

namespace FoodDeliveryService.Modules.Payments.Domain.Refunds;

/// <summary>
/// The bounded set of reasons a refund does not happen — Feature 3.8 Milestone H, the same
/// discipline as <see cref="Payments.PaymentFailureReason"/> and for the same two reasons: the value
/// reaches a metric tag (§11.1), and it is written into Support's audit log where a human reads it.
/// Stripe's own message never appears in either.
/// <para>
/// Three of the four are refusals this platform makes before calling anybody, which is the useful
/// distinction when one of these turns up in the log: only <see cref="GatewayError"/> means the
/// provider was asked and said no.
/// </para>
/// </summary>
public static class RefundFailureReason
{
    /// <summary>
    /// The order was paid in cash, so there is no <c>Payment</c> row and nothing this service can
    /// give back. The approval stands and the money has to move in the world instead — which is
    /// exactly why this is published rather than swallowed: a request that stays "approved" forever
    /// tells the agent who raised it nothing.
    /// </summary>
    public const string NotCardPayment = "not_card_payment";

    /// <summary>
    /// The hold was never taken — the order was rejected or cancelled, or the capture never
    /// happened. Releasing an authorization is not a refund and there is nothing to return.
    /// </summary>
    public const string PaymentNotCaptured = "payment_not_captured";

    /// <summary>
    /// More was asked for than is left. Support caps a request at the order subtotal when it is
    /// raised; this is the second, authoritative cap, against what was actually captured minus what
    /// has already gone back (§10.1).
    /// </summary>
    public const string AmountExceedsCaptured = "amount_exceeds_captured";

    /// <summary>
    /// The provider was asked and did not do it — a refusal, a rejected request, an unmapped error.
    /// One bucket, like its counterpart on the authorization side: the detail is in the logged
    /// <c>StripeError</c>, and splitting it would put our own bug taxonomy on a metric tag.
    /// </summary>
    public const string GatewayError = "gateway_error";

    public static readonly FrozenSet<string> All = new[]
    {
        NotCardPayment,
        PaymentNotCaptured,
        AmountExceedsCaptured,
        GatewayError
    }.ToFrozenSet(StringComparer.Ordinal);

    public static bool IsKnown(string reason) => All.Contains(reason);
}

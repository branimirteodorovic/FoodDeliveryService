using System.Collections.Frozen;

namespace FoodDeliveryService.Modules.Payments.Domain.Payments;

/// <summary>
/// The bounded set of reasons an authorization can fail — §8.4. Every failure the platform reports,
/// publishes on <c>PaymentAuthorizationFailedIntegrationEvent</c>, emails about or counts in
/// <c>payments.failed</c> is one of these five strings.
/// <para>
/// Bounded is the operative word. Stripe's own <c>StripeError.Message</c> is unbounded free text
/// ("Your card was declined. Your request was in live mode, but used a known test card." and worse),
/// and putting it on a metric tag makes one time series per distinct message — the classic
/// cardinality explosion that takes Prometheus down rather than the service. It is also
/// customer-facing text written by a third party, which is not text this platform should be
/// forwarding unread.
/// </para>
/// <para>
/// Declared in Milestone C rather than in §8 because <c>StripePaymentGateway</c> is what maps a
/// Stripe decline code onto one of these, and the mapping is the seam's job.
/// </para>
/// </summary>
public static class PaymentFailureReason
{
    /// <summary>The issuer declined without saying why — Stripe's generic <c>card_declined</c>.</summary>
    public const string CardDeclined = "card_declined";

    /// <summary>The issuer declined for want of funds. Test card 4000 0000 0000 9995.</summary>
    public const string InsufficientFunds = "insufficient_funds";

    public const string ExpiredCard = "expired_card";

    /// <summary>
    /// The card wants 3-D Secure and the charge is off-session, so there is no browser to challenge.
    /// Test card 4000 0025 0000 3155. An on-session retry — notify the customer, have them
    /// re-authenticate, resume the intent — is frontend work and is explicitly out of scope (§8.5).
    /// </summary>
    public const string AuthenticationRequired = "authentication_required";

    /// <summary>
    /// Everything that is not the card's fault: a malformed request, a rejected API key, an
    /// unmapped Stripe error. Deliberately one bucket — the operator learns the detail from the
    /// logged <c>StripeError</c>, and splitting it would put our own bug taxonomy on a metric tag.
    /// </summary>
    public const string GatewayError = "gateway_error";

    public static readonly FrozenSet<string> All = new[]
    {
        CardDeclined,
        InsufficientFunds,
        ExpiredCard,
        AuthenticationRequired,
        GatewayError
    }.ToFrozenSet(StringComparer.Ordinal);

    public static bool IsKnown(string reason) => All.Contains(reason);
}

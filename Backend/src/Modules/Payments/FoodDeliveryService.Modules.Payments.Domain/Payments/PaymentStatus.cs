namespace FoodDeliveryService.Modules.Payments.Domain.Payments;

/// <summary>
/// Where one order's money is — Feature 3.8 Milestone F, §8.1.
/// <para>
/// It is deliberately <b>not</b> a mirror of Stripe's <c>PaymentIntent.status</c>. That vocabulary
/// is the provider's and lives in <see cref="Application.Abstractions.Payments.GatewayPaymentIntentStatus"/>;
/// this one is the platform's, and the two differ where it matters — Stripe calls a captured intent
/// <c>succeeded</c> and a released one <c>canceled</c>, neither of which says what happened to the
/// order behind it.
/// </para>
/// <para>
/// <b>Four of the six are terminal</b> (<see cref="Captured"/>, <see cref="Released"/>,
/// <see cref="Failed"/>, <see cref="Refunded"/>) and every mutation on the aggregate returns success
/// without acting once it is in one of them — §8.6. That is not politeness: the outbox and the
/// webhook both drive these transitions, both are at-least-once, and a second capture of a captured
/// payment is a second charge.
/// </para>
/// </summary>
public enum PaymentStatus
{
    /// <summary>
    /// The row exists and the provider has not answered yet. Written <b>before</b> the Stripe call,
    /// so a crash between the call and its result leaves something for the reconciling webhook to
    /// find (§7) — a payment the provider knows about and this platform does not is the one state
    /// that cannot be recovered from.
    /// </summary>
    Authorizing = 1,

    /// <summary>The funds are held on the card and have not moved. The happy state after §8.</summary>
    Authorized = 2,

    /// <summary>The money has actually been taken — §9, on the restaurant accepting.</summary>
    Captured = 3,

    /// <summary>The hold was cancelled without ever being charged — §9, on a reject or a cancel.</summary>
    Released = 4,

    /// <summary>
    /// The authorization did not happen: a decline, an off-session 3-D Secure challenge, no saved
    /// card, or a provider refusal. <see cref="Payment.FailureReason"/> carries which, bounded to
    /// <see cref="PaymentFailureReason"/>.
    /// </summary>
    Failed = 5,

    /// <summary>Captured, then given back — §10. Reached only from <see cref="Captured"/>.</summary>
    Refunded = 6
}

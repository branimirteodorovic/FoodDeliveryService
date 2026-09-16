namespace FoodDeliveryService.Modules.Payments.Application.Abstractions.Payments;

/// <summary>
/// The provider event types this module subscribes to — the dispatch table's keys.
/// <para>
/// They are Stripe's strings and they are not translated, because they are also what is configured
/// in the Stripe dashboard and printed by <c>stripe listen</c>: a platform-local synonym would make
/// the two impossible to compare when a delivery goes missing.
/// </para>
/// <para>
/// An event type not named here is still <em>recorded</em> — the log is a record of what the
/// provider said, not of what this module chose to act on — and simply has no work attached. That
/// asymmetry is deliberate: turning an unknown type into a failure would mean every event type
/// somebody enables in the dashboard takes the endpoint to a <c>400</c> and Stripe into backoff.
/// </para>
/// </summary>
public static class PaymentWebhookEventTypes
{
    /// <summary>
    /// The browser confirmed a SetupIntent and the card is now attached at Stripe — §1.3 step 1.
    /// <b>This event is the attachment</b>, not the endpoint that created the SetupIntent.
    /// </summary>
    public const string SetupIntentSucceeded = "setup_intent.succeeded";

    /// <summary>
    /// A manual-capture intent now has funds held on it — the provider's own account of what
    /// <c>AuthorizeAsync</c> just did, arriving independently of that call's response (Milestone F).
    /// <para>
    /// In the ordinary case it is redundant and the arm is a no-op, because the API response already
    /// authorized the payment. It is not redundant in the case that matters: the response was lost,
    /// the process died, or the transaction that would have recorded it rolled back. Neither outbox
    /// job retries (§5.6), so this event is the <em>only</em> thing that finishes such a payment —
    /// which is what makes §7 a correctness requirement of §8 rather than a refinement of it.
    /// </para>
    /// </summary>
    public const string PaymentIntentAmountCapturableUpdated = "payment_intent.amount_capturable_updated";

    /// <summary>
    /// The charge did not go through — a decline, an expired card, an off-session 3-D Secure
    /// challenge. The same reconciling role as the arm above, for the unhappy outcome.
    /// </summary>
    public const string PaymentIntentPaymentFailed = "payment_intent.payment_failed";
}

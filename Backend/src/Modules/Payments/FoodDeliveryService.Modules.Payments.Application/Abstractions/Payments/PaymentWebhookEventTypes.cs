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
}

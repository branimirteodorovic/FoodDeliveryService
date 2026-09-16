namespace FoodDeliveryService.Modules.Payments.Application.Abstractions.Payments;

/// <summary>
/// The provider's own <c>PaymentIntent.status</c> strings, for the two values a webhook arm decides
/// from — Feature 3.8 Milestone F.
/// <para>
/// They live here, untranslated, for the same reason <see cref="PaymentWebhookEventTypes"/>'s
/// strings do: <c>stripe_event_logs.object_status</c> records the status <em>as the payload reported
/// it</em> (§7.5), because that is a fact about the object rather than an interpretation of it — and
/// a fact recorded in the provider's vocabulary is one an operator can compare against the Stripe
/// dashboard without a translation table.
/// </para>
/// <para>
/// Note the division of labour. <see cref="GatewayPaymentIntentStatus"/> is the mapped, neutral
/// enum, and it is what an <em>API response</em> is read through, because the SDK hands the seam a
/// typed object. A webhook payload arrives as recorded text on a row, so these are what it is
/// compared against. Two representations of one vocabulary, each on the side of the seam where the
/// value actually exists.
/// </para>
/// </summary>
public static class PaymentIntentStatuses
{
    /// <summary>Funds are held on the card and have not moved — a manual-capture authorization.</summary>
    public const string RequiresCapture = "requires_capture";

    /// <summary>
    /// What a failed off-session charge leaves behind: Stripe detaches the method that failed and
    /// the intent falls back to wanting one. The usual status on a
    /// <c>payment_intent.payment_failed</c> — named here for the reader, not branched on: the
    /// failure arm refuses only the payload that contradicts itself by reporting
    /// <see cref="RequiresCapture"/>.
    /// </summary>
    public const string RequiresPaymentMethod = "requires_payment_method";
}

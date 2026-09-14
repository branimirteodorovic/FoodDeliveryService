namespace FoodDeliveryService.Modules.Payments.Application.Abstractions.Payments;

/// <summary>
/// A verified provider event, reduced to the facts this module acts on — Feature 3.8 Milestone E.
/// <para>
/// Provider-neutral for the same reason <see cref="GatewayPaymentIntent"/> is: the Stripe SDK stops
/// at <c>Payments.Infrastructure</c>, and a <c>Stripe.Event</c> handed to an Application handler
/// would put the SDK's object graph — and its API keys' worth of surface — into every layer above
/// the seam.
/// </para>
/// <para>
/// Most fields are nullable because most events do not carry most of them: a
/// <c>setup_intent.succeeded</c> names a customer and a payment method and no amount, a
/// <c>payment_intent.*</c> names an amount and no payment method. A missing field is a fact about
/// the event rather than a parse failure, so it is carried and checked where it is needed.
/// </para>
/// </summary>
/// <param name="EventId">Stripe's <c>evt_…</c> — the dedupe key (§7.4).</param>
/// <param name="EventType">The dispatch key, e.g. <c>setup_intent.succeeded</c>.</param>
/// <param name="ObjectId">The object the event is about: <c>seti_…</c>, <c>pi_…</c>, <c>re_…</c>.</param>
/// <param name="ObjectStatus">
/// That object's status <em>on this payload</em>. §7.5: decide from this, never from the order the
/// deliveries arrived in.
/// </param>
/// <param name="CustomerReference">The <c>cus_…</c> the event names, if any.</param>
/// <param name="PaymentMethodReference">The <c>pm_…</c> the event names, if any.</param>
public sealed record PaymentWebhookEvent(
    string EventId,
    string EventType,
    string? ObjectId,
    string? ObjectStatus,
    string? CustomerReference,
    string? PaymentMethodReference);

namespace FoodDeliveryService.Modules.Payments.Application.Abstractions.Payments;

/// <summary>
/// What authorize, capture and release all return. One shape for the three so a caller reads the
/// status the same way whichever transition it just drove, and so §8.6's terminal-state no-ops have
/// something to compare against.
/// </summary>
/// <param name="PaymentIntentId">The <c>pi_…</c> identifier. Stripe is the source of truth; this is the handle to it.</param>
/// <param name="Status">Decide from this, never from the order calls or webhooks arrived in (§7.5).</param>
/// <param name="AmountMinorUnits">
/// Integer minor units, as Stripe reports them. After an authorization it is the amount held; after
/// a capture, the amount actually taken. Compared against <c>Money.ToMinorUnits()</c> rather than
/// converted back, so a rounding disagreement surfaces as an inequality instead of being averaged
/// away.
/// </param>
public sealed record GatewayPaymentIntent(
    string PaymentIntentId,
    GatewayPaymentIntentStatus Status,
    long AmountMinorUnits);

using FoodDeliveryService.Common.Application.Caching;

namespace FoodDeliveryService.Modules.Payments.Application.Abstractions.Payments;

/// <summary>
/// The one resource every payment mutation serializes on, and how long an acquisition survives —
/// Feature 3.8 Milestone F, §8.1 and rule 2 of §1.4.
/// <para>
/// <b>One key, built in one place, because there are two writers.</b> The outbox handler acting on
/// <c>OrderPlacedIntegrationEvent</c> and the webhook arms acting on <c>payment_intent.*</c> are two
/// processes racing on one row, every transition is check-then-act (read the status, decide, write),
/// and no aggregate in this codebase carries an optimistic concurrency token — so the database will
/// not reject the second write. If the two sides ever built different key strings they would both
/// acquire, both succeed, and the lock would be decorative.
/// </para>
/// <para>
/// <b>Keyed on the order, not the payment.</b> The order id is the one identifier both sides have
/// before they read anything: the outbox side is handed it by the event, and the webhook side reads
/// it off the recorded event's <c>order_reference</c> (Stripe metadata this service set itself when
/// it created the intent). Keying on the payment id would mean reading the payment to find out which
/// lock to take, which is the read the lock is supposed to be protecting.
/// </para>
/// </summary>
public static class PaymentLocks
{
    /// <summary>
    /// Long, by this codebase's standards — <c>DeliveryLocks.Ttl</c> is five seconds — and
    /// deliberately so: this critical section contains a **provider round trip**, not a geo search
    /// and a local transaction. A TTL shorter than a slow Stripe call would expire under the holder
    /// mid-authorization, which is precisely when a second caller must not be let in.
    /// <para>
    /// Thirty seconds is also the whole budget: <c>StripePaymentGateway</c> lets the SDK's own
    /// timeout end a hung call long before this lapses, so a crashed holder blocks one order's
    /// payment for half a minute and nothing else. And should the TTL ever be outlived anyway, the
    /// idempotency key on the call underneath (rule 1) is what stops the second caller charging the
    /// card twice — the two rules in §1.4 are one behind the other on purpose.
    /// </para>
    /// </summary>
    public static readonly TimeSpan Ttl = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Held for the whole of one order's payment transition — authorize, capture (§9), release (§9)
    /// and refund (§10) all take this same key.
    /// </summary>
    public static string Payment(Guid orderId) => CacheKeys.Create("payments", "payment-lock", orderId);
}

namespace FoodDeliveryService.Modules.Payments.Application.Abstractions.Payments;

/// <summary>
/// The five idempotency keys this feature ever sends, in one place — §5.2.
/// <para>
/// Every key is derived from an identifier that is already durable: the order, the refund request,
/// the user. That is the property that matters. <c>ProcessOutboxJob</c> and <c>ProcessInboxJob</c>
/// are at-least-once, the webhook handler and the outbox handler can drive the same transition, and
/// a retried authorization without a key is a second charge on a real card. A key built from
/// <c>Guid.NewGuid()</c> or a timestamp is *worse* than no key, because it looks like the problem
/// has been dealt with.
/// </para>
/// <para>
/// One operation per order, so <c>order-auth-{orderId}</c> is unambiguous: an order is authorized
/// once, captured once and released once. Stripe keeps an idempotency key for 24 hours, which is
/// longer than any retry this platform performs.
/// </para>
/// </summary>
public static class PaymentIdempotencyKeys
{
    public static string Authorize(Guid orderId) => $"order-auth-{orderId}";

    public static string Capture(Guid orderId) => $"order-capture-{orderId}";

    public static string Release(Guid orderId) => $"order-release-{orderId}";

    public static string Refund(Guid refundRequestId) => $"refund-{refundRequestId}";

    public static string Customer(Guid userId) => $"customer-{userId}";

    /// <summary>
    /// Starting card collection — Milestone D, §6.3. <b>The one key here not derived from a durable
    /// id</b>, and the exception is argued rather than assumed.
    /// <para>
    /// Every other operation on this type guards money: a retry without a replayed key is a second
    /// charge, a second capture or a second refund. Creating a SetupIntent moves nothing. It is
    /// driven by a synchronous request from a person who is looking at the response, no job retries
    /// it, and a duplicate SetupIntent nobody confirms is inert and expires on its own.
    /// </para>
    /// <para>
    /// The durable-id alternatives are all worse. Keying on the customer replays one client secret
    /// for 24 hours — including after it has been consumed — so a customer who removes a card and
    /// adds another the same day is handed a secret that Stripe.js rejects. Keying on a timestamp is
    /// the anti-pattern §5.2 names. So: a fresh attempt id per call, deliberately, here and
    /// <em>only</em> here.
    /// </para>
    /// </summary>
    public static string SetupIntent(Guid attemptId) => $"setup-{attemptId}";

    /// <summary>
    /// Attaching a saved card — Milestone D. §5.2 fixed five key formats; these two are a sixth and
    /// a seventh, added because §6.3's endpoints mutate at the provider and
    /// <see cref="IPaymentGateway"/> takes a key on every method by construction.
    /// <para>
    /// They are keyed on the <c>pm_…</c> identifier rather than on the customer, which is the
    /// durable id for <em>this</em> operation: a customer attaches, detaches and re-attaches over
    /// time, so <c>pm-attach-{customerId}</c> would replay the first card's response forever.
    /// </para>
    /// </summary>
    public static string AttachPaymentMethod(string paymentMethodId) => $"pm-attach-{paymentMethodId}";

    /// <summary>Detaching a saved card — see <see cref="AttachPaymentMethod"/>.</summary>
    public static string DetachPaymentMethod(string paymentMethodId) => $"pm-detach-{paymentMethodId}";
}

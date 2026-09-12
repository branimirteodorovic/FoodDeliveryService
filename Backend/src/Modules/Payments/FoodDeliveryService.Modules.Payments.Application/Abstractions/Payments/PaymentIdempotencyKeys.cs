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
}

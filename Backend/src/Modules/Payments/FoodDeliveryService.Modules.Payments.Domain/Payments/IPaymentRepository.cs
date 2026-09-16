namespace FoodDeliveryService.Modules.Payments.Domain.Payments;

public interface IPaymentRepository
{
    /// <summary>
    /// The one read that returns a payment to act on. The order is the natural handle — one order
    /// has at most one payment — and it is also the lock key (<c>PaymentLocks.Payment</c>), so every
    /// authoritative read happens under the lock that protects it.
    /// </summary>
    Task<Payment?> GetByOrderIdAsync(Guid orderId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Resolves a provider intent to the order behind it, and deliberately returns the <b>id</b>
    /// rather than the payment.
    /// <para>
    /// This is the webhook side's way in: a <c>payment_intent.*</c> names a <c>pi_…</c> and nothing
    /// of this platform's, and the lock key is the order id, so the key has to be discovered before
    /// the lock can be taken. Returning an id keeps that discovery from becoming the read it is
    /// supposed to precede — an <em>entity</em> read here would be tracked, and the authoritative
    /// read inside the lock would then be served the pre-lock snapshot out of the identity map
    /// without touching the database at all.
    /// </para>
    /// <para>
    /// Only needed when the event carries no <c>order_reference</c> metadata; see the arms in
    /// <c>Application/Webhooks</c>.
    /// </para>
    /// </summary>
    Task<Guid?> FindOrderIdByPaymentIntentIdAsync(
        string stripePaymentIntentId,
        CancellationToken cancellationToken = default);

    void Insert(Payment payment);
}

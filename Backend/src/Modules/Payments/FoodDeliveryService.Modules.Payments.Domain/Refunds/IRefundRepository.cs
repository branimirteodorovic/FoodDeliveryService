namespace FoodDeliveryService.Modules.Payments.Domain.Refunds;

public interface IRefundRepository
{
    /// <summary>
    /// The idempotency read. Support's request id is the unique key of the table, so a redelivered
    /// approval finds the row the first delivery wrote rather than refunding a second time.
    /// </summary>
    Task<Refund?> GetByRefundRequestIdAsync(
        Guid refundRequestId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// What has already gone back for this order, in major units — the second half of §10.1's
    /// ceiling, the first being the captured amount on the payment.
    /// <para>
    /// <b>Settled refunds only.</b> A failed one returned nothing and must not consume the
    /// allowance; a pending one is a refund in flight for which this service is the only writer, and
    /// it is written inside the same lock this read happens under, so it cannot be mid-flight while
    /// another caller is reading. Counting it would double-count the caller's own row.
    /// </para>
    /// <para>
    /// Keyed on the order, like the lock: one order has one payment, and the ceiling is a property
    /// of that payment rather than of any single refund.
    /// </para>
    /// </summary>
    Task<decimal> GetSettledTotalForOrderAsync(Guid orderId, CancellationToken cancellationToken = default);

    void Insert(Refund refund);
}

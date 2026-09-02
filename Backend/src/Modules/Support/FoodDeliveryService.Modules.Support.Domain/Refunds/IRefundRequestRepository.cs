namespace FoodDeliveryService.Modules.Support.Domain.Refunds;

public interface IRefundRequestRepository
{
    Task<RefundRequest?> GetAsync(Guid refundRequestId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Whether the order already carries a refund that is awaiting a decision or has been approved.
    /// A rejected one deliberately does not count — a second agent may legitimately ask again with
    /// a better case, and a permanent lock-out after one refusal would be a worse rule than none.
    /// <para>
    /// This is the read half of the at-most-one rule; the unique partial index on the table is the
    /// half that holds under concurrency.
    /// </para>
    /// </summary>
    Task<bool> HasActiveForOrderAsync(Guid orderId, CancellationToken cancellationToken = default);

    void Insert(RefundRequest refundRequest);
}

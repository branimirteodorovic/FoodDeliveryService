namespace FoodDeliveryService.Modules.Payments.Domain.Webhooks;

public interface IStripeEventLogRepository
{
    Task<StripeEventLog?> GetAsync(Guid eventLogId, CancellationToken cancellationToken = default);

    /// <summary>
    /// The first of the two dedupe layers (§7.4): the cheap read that catches a redelivery without
    /// provoking a constraint violation. The unique index behind
    /// <see cref="ExistsAsync"/>'s blind spot — two deliveries in flight at once — is the second.
    /// </summary>
    Task<bool> ExistsAsync(string providerEventId, CancellationToken cancellationToken = default);

    void Insert(StripeEventLog eventLog);
}

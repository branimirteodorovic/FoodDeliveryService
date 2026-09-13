using FoodDeliveryService.Common.Application.EventBus;

namespace FoodDeliveryService.Modules.Payments.IntegrationEvents;

/// <summary>
/// A customer removed their saved card and can no longer be charged off-session — §6.3.
/// <para>
/// Consumers clear the flag they set from <see cref="PaymentMethodAttachedIntegrationEvent"/>. The
/// pair is what makes the replica converge: a consumer that only handled the attach would keep
/// offering card payment to a customer who has none, forever.
/// </para>
/// </summary>
public sealed class PaymentMethodDetachedIntegrationEvent : IntegrationEvent
{
    public PaymentMethodDetachedIntegrationEvent(
        Guid id,
        DateTime occurredOnUtc,
        Guid customerId,
        Guid paymentMethodId,
        DateTime detachedOnUtc)
        : base(id, occurredOnUtc)
    {
        CustomerId = customerId;
        PaymentMethodId = paymentMethodId;
        DetachedOnUtc = detachedOnUtc;
    }

    public Guid CustomerId { get; init; }

    public Guid PaymentMethodId { get; init; }

    public DateTime DetachedOnUtc { get; init; }
}

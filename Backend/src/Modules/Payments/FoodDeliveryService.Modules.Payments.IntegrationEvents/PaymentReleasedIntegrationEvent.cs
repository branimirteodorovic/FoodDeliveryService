using FoodDeliveryService.Common.Application.EventBus;

namespace FoodDeliveryService.Modules.Payments.IntegrationEvents;

/// <summary>
/// The hold on the customer's card is gone and nothing was ever charged — Feature 3.8 Milestone G,
/// §1.3 step 6. Published when the restaurant rejected the order or the customer cancelled it.
/// <para>
/// <b>It is not a refund and must never be described as one to a customer.</b> No money moved, so
/// there is no transaction to reverse and nothing will appear on a statement; what recovers is the
/// available balance the authorization was holding, on the issuer's own schedule. The refund
/// contract is <c>RefundSettled</c> in §10 and it starts from a captured payment.
/// </para>
/// </summary>
public sealed class PaymentReleasedIntegrationEvent : IntegrationEvent
{
    public PaymentReleasedIntegrationEvent(
        Guid id,
        DateTime occurredOnUtc,
        Guid paymentId,
        Guid orderId,
        Guid customerId,
        decimal amount,
        string currency,
        DateTime releasedOnUtc)
        : base(id, occurredOnUtc)
    {
        PaymentId = paymentId;
        OrderId = orderId;
        CustomerId = customerId;
        Amount = amount;
        Currency = currency;
        ReleasedOnUtc = releasedOnUtc;
    }

    public Guid PaymentId { get; init; }

    public Guid OrderId { get; init; }

    public Guid CustomerId { get; init; }

    /// <summary>The amount that was held and has now been let go, in major units.</summary>
    public decimal Amount { get; init; }

    /// <summary>ISO 4217, upper case.</summary>
    public string Currency { get; init; }

    public DateTime ReleasedOnUtc { get; init; }
}

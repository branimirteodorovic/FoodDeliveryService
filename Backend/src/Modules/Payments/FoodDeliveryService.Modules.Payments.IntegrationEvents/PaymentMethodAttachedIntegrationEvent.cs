using FoodDeliveryService.Common.Application.EventBus;

namespace FoodDeliveryService.Modules.Payments.IntegrationEvents;

/// <summary>
/// A customer saved a card and can now be charged off-session — Feature 3.8 Milestone D, §6.3.
/// <para>
/// <b>The Stripe payment method identifier is deliberately not on this contract.</b> Every consumer
/// needs one fact — "this customer can pay by card" — and a <c>pm_…</c> replicated into other
/// services' databases is a provider credential in places that can do nothing with it except leak
/// it. The display fields are here because a consumer may want to render "Visa ending 4242" without
/// a call back (hard rule #9); the authoritative check always stays in Payments.
/// </para>
/// </summary>
public sealed class PaymentMethodAttachedIntegrationEvent : IntegrationEvent
{
    public PaymentMethodAttachedIntegrationEvent(
        Guid id,
        DateTime occurredOnUtc,
        Guid customerId,
        Guid paymentMethodId,
        string? brand,
        string? last4,
        int? expiryMonth,
        int? expiryYear,
        DateTime attachedOnUtc)
        : base(id, occurredOnUtc)
    {
        CustomerId = customerId;
        PaymentMethodId = paymentMethodId;
        Brand = brand;
        Last4 = last4;
        ExpiryMonth = expiryMonth;
        ExpiryYear = expiryYear;
        AttachedOnUtc = attachedOnUtc;
    }

    public Guid CustomerId { get; init; }

    /// <summary>This platform's identifier for the card — not Stripe's.</summary>
    public Guid PaymentMethodId { get; init; }

    public string? Brand { get; init; }

    public string? Last4 { get; init; }

    public int? ExpiryMonth { get; init; }

    public int? ExpiryYear { get; init; }

    public DateTime AttachedOnUtc { get; init; }
}

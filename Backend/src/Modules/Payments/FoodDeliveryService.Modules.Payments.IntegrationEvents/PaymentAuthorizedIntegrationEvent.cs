using FoodDeliveryService.Common.Application.EventBus;

namespace FoodDeliveryService.Modules.Payments.IntegrationEvents;

/// <summary>
/// The customer's card is holding the order's money — Feature 3.8 Milestone F, §1.3 step 4.
/// <para>
/// Orders projects it onto its own <c>PaymentStatus</c>, which is what lifts the guard on
/// <c>Order.Accept()</c>. Nothing has been charged: the capture happens when the restaurant accepts
/// (§9), and a hold that is never captured is released rather than refunded.
/// </para>
/// <para>
/// <b>No <c>pi_…</c>.</b> The amount and the parties are the full snapshot a consumer needs (hard
/// rule #9); the provider's identifier is this service's business alone, the same rule
/// <c>PaymentMethodAttachedIntegrationEvent</c> follows for <c>pm_…</c>.
/// </para>
/// </summary>
public sealed class PaymentAuthorizedIntegrationEvent : IntegrationEvent
{
    public PaymentAuthorizedIntegrationEvent(
        Guid id,
        DateTime occurredOnUtc,
        Guid paymentId,
        Guid orderId,
        Guid customerId,
        decimal amount,
        string currency,
        DateTime authorizedOnUtc)
        : base(id, occurredOnUtc)
    {
        PaymentId = paymentId;
        OrderId = orderId;
        CustomerId = customerId;
        Amount = amount;
        Currency = currency;
        AuthorizedOnUtc = authorizedOnUtc;
    }

    public Guid PaymentId { get; init; }

    public Guid OrderId { get; init; }

    public Guid CustomerId { get; init; }

    /// <summary>The amount held, in major units — 12.99, not 1299.</summary>
    public decimal Amount { get; init; }

    /// <summary>ISO 4217, upper case. One value ever flows through it today (§0.4), and it is still carried.</summary>
    public string Currency { get; init; }

    public DateTime AuthorizedOnUtc { get; init; }
}

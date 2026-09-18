using FoodDeliveryService.Common.Application.EventBus;

namespace FoodDeliveryService.Modules.Payments.IntegrationEvents;

/// <summary>
/// The order's money has been taken — Feature 3.8 Milestone G, §1.3 step 5. Published when the
/// restaurant accepted the order and the hold from §8 was captured.
/// <para>
/// Orders projects it onto its own <c>PaymentStatus</c>, which drives nothing locally: by the time
/// this arrives the order is already <c>Accepted</c> and on its way. What the column buys is a
/// customer, an agent and an operator all being able to answer "has this been charged?" without
/// asking another service — and §10's refund ceiling, which starts from a captured amount.
/// </para>
/// <para>
/// <b>No <c>pi_…</c>, same as its siblings.</b> The amount and the parties are the full snapshot
/// (hard rule #9); the provider's identifier stays in this service.
/// </para>
/// </summary>
public sealed class PaymentCapturedIntegrationEvent : IntegrationEvent
{
    public PaymentCapturedIntegrationEvent(
        Guid id,
        DateTime occurredOnUtc,
        Guid paymentId,
        Guid orderId,
        Guid customerId,
        decimal amount,
        string currency,
        DateTime capturedOnUtc)
        : base(id, occurredOnUtc)
    {
        PaymentId = paymentId;
        OrderId = orderId;
        CustomerId = customerId;
        Amount = amount;
        Currency = currency;
        CapturedOnUtc = capturedOnUtc;
    }

    public Guid PaymentId { get; init; }

    public Guid OrderId { get; init; }

    public Guid CustomerId { get; init; }

    /// <summary>The amount charged, in major units — 12.99, not 1299.</summary>
    public decimal Amount { get; init; }

    /// <summary>ISO 4217, upper case.</summary>
    public string Currency { get; init; }

    public DateTime CapturedOnUtc { get; init; }
}

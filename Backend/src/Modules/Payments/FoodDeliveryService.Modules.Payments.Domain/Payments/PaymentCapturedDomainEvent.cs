using FoodDeliveryService.Common.Domain;

namespace FoodDeliveryService.Modules.Payments.Domain.Payments;

/// <summary>
/// The money has actually moved — Feature 3.8 Milestone G, §9. Raised when the restaurant accepted
/// the order and the hold placed at §8 was taken.
/// <para>
/// It carries the same snapshot as <see cref="PaymentAuthorizedDomainEvent"/> and for the same
/// reasons: the outbox handler that publishes it runs long after the transaction that raised it, so
/// nothing is read back, and the <c>pi_…</c> stays inside this service.
/// </para>
/// </summary>
public sealed class PaymentCapturedDomainEvent(
    Guid paymentId,
    Guid orderId,
    Guid customerId,
    decimal amount,
    string currency,
    DateTime capturedOnUtc) : DomainEvent
{
    public Guid PaymentId { get; init; } = paymentId;

    public Guid OrderId { get; init; } = orderId;

    public Guid CustomerId { get; init; } = customerId;

    public decimal Amount { get; init; } = amount;

    public string Currency { get; init; } = currency;

    public DateTime CapturedOnUtc { get; init; } = capturedOnUtc;
}

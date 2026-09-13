using FoodDeliveryService.Common.Domain;

namespace FoodDeliveryService.Modules.Payments.Domain.PaymentMethods;

/// <summary>
/// A customer saved a card. Carries the display fields rather than only the customer id, so the
/// handler that builds the integration event does not have to read the aggregate back — the outbox
/// dispatches it in a different transaction, by which time the card may already have been replaced.
/// <para>
/// The <c>pm_…</c> identifier is deliberately absent, here and on the integration event built from
/// it (§6.3).
/// </para>
/// </summary>
public sealed class PaymentMethodAttachedDomainEvent(
    Guid customerId,
    Guid paymentMethodId,
    string? brand,
    string? last4,
    int? expiryMonth,
    int? expiryYear,
    DateTime attachedOnUtc) : DomainEvent
{
    public Guid CustomerId { get; init; } = customerId;

    public Guid PaymentMethodId { get; init; } = paymentMethodId;

    public string? Brand { get; init; } = brand;

    public string? Last4 { get; init; } = last4;

    public int? ExpiryMonth { get; init; } = expiryMonth;

    public int? ExpiryYear { get; init; } = expiryYear;

    public DateTime AttachedOnUtc { get; init; } = attachedOnUtc;
}

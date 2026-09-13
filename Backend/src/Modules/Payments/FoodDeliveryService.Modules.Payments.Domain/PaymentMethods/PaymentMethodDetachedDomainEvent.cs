using FoodDeliveryService.Common.Domain;

namespace FoodDeliveryService.Modules.Payments.Domain.PaymentMethods;

/// <summary>
/// A customer removed their saved card. The counterpart of
/// <see cref="PaymentMethodAttachedDomainEvent"/>: it is what tells Orders to stop accepting card
/// orders from this customer, and an order placed in the window before that projection lands is
/// refused by Payments rather than charged to a card that is gone.
/// </summary>
public sealed class PaymentMethodDetachedDomainEvent(
    Guid customerId,
    Guid paymentMethodId,
    DateTime detachedOnUtc) : DomainEvent
{
    public Guid CustomerId { get; init; } = customerId;

    public Guid PaymentMethodId { get; init; } = paymentMethodId;

    public DateTime DetachedOnUtc { get; init; } = detachedOnUtc;
}

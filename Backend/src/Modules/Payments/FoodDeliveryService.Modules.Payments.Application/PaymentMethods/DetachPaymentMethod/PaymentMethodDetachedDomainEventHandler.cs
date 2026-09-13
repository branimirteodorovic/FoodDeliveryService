using FoodDeliveryService.Common.Application.EventBus;
using FoodDeliveryService.Common.Application.Messaging;
using FoodDeliveryService.Modules.Payments.Domain.PaymentMethods;
using FoodDeliveryService.Modules.Payments.IntegrationEvents;

namespace FoodDeliveryService.Modules.Payments.Application.PaymentMethods.DetachPaymentMethod;

/// <summary>
/// Tells Orders to stop offering card payment to this customer. The window between the removal and
/// the projection landing is real but harmless: an order placed inside it is accepted by Orders and
/// then refused by this service at authorization, which is the failure the replica exists to make
/// rare rather than the one it claims to make impossible.
/// </summary>
internal sealed class PaymentMethodDetachedDomainEventHandler(IEventBus eventBus)
    : DomainEventHandler<PaymentMethodDetachedDomainEvent>
{
    public override async Task Handle(
        PaymentMethodDetachedDomainEvent domainEvent,
        CancellationToken cancellationToken = default)
    {
        await eventBus.PublishAsync(
            new PaymentMethodDetachedIntegrationEvent(
                domainEvent.Id,
                domainEvent.OccurredOnUtc,
                domainEvent.CustomerId,
                domainEvent.PaymentMethodId,
                domainEvent.DetachedOnUtc),
            cancellationToken);
    }
}

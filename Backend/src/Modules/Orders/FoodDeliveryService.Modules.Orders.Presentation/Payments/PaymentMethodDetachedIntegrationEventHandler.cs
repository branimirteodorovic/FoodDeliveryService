using FoodDeliveryService.Common.Application.EventBus;
using FoodDeliveryService.Common.Domain;
using FoodDeliveryService.Modules.Orders.Application.Customers.SetCardPaymentAvailability;
using FoodDeliveryService.Modules.Payments.IntegrationEvents;
using MediatR;

namespace FoodDeliveryService.Modules.Orders.Presentation.Payments;

/// <summary>
/// Clears the flag its counterpart set. Handling only the attach would leave every customer who ever
/// saved a card marked as able to pay by one, permanently — the replica would only ever converge
/// upwards.
/// </summary>
internal sealed class PaymentMethodDetachedIntegrationEventHandler(ISender sender)
    : IntegrationEventHandler<PaymentMethodDetachedIntegrationEvent>
{
    public override async Task Handle(
        PaymentMethodDetachedIntegrationEvent integrationEvent,
        CancellationToken cancellationToken = default)
    {
        Result result = await sender.Send(
            new SetCardPaymentAvailabilityCommand(
                integrationEvent.CustomerId,
                CanPayByCard: false,
                integrationEvent.DetachedOnUtc),
            cancellationToken);

        if (result.IsFailure)
        {
            throw new Common.Application.Exceptions.ApplicationException(
                nameof(SetCardPaymentAvailabilityCommand),
                result.Error);
        }
    }
}

using FoodDeliveryService.Common.Application.EventBus;
using FoodDeliveryService.Common.Domain;
using FoodDeliveryService.Modules.Orders.Application.Customers.SetCardPaymentAvailability;
using FoodDeliveryService.Modules.Payments.IntegrationEvents;
using MediatR;

namespace FoodDeliveryService.Modules.Orders.Presentation.Payments;

/// <summary>
/// Records that this customer can pay by card. The event carries the brand and last four digits too,
/// and Orders deliberately keeps neither: they belong to the screen that manages cards, and
/// replicating a card detail into a second database is a second place it has to be protected.
/// </summary>
internal sealed class PaymentMethodAttachedIntegrationEventHandler(ISender sender)
    : IntegrationEventHandler<PaymentMethodAttachedIntegrationEvent>
{
    public override async Task Handle(
        PaymentMethodAttachedIntegrationEvent integrationEvent,
        CancellationToken cancellationToken = default)
    {
        Result result = await sender.Send(
            new SetCardPaymentAvailabilityCommand(
                integrationEvent.CustomerId,
                CanPayByCard: true,
                integrationEvent.AttachedOnUtc),
            cancellationToken);

        if (result.IsFailure)
        {
            throw new Common.Application.Exceptions.ApplicationException(
                nameof(SetCardPaymentAvailabilityCommand),
                result.Error);
        }
    }
}

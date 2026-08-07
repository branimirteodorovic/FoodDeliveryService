using FoodDeliveryService.Common.Application.EventBus;
using FoodDeliveryService.Common.Domain;
using FoodDeliveryService.Modules.Delivery.IntegrationEvents;
using FoodDeliveryService.Modules.FraudDetection.Application.Deliveries.RecordOrderPickedUp;
using MediatR;

namespace FoodDeliveryService.Modules.FraudDetection.Presentation.Deliveries;

internal sealed class OrderPickedUpIntegrationEventHandler(ISender sender)
    : IntegrationEventHandler<OrderPickedUpIntegrationEvent>
{
    public override async Task Handle(
        OrderPickedUpIntegrationEvent integrationEvent,
        CancellationToken cancellationToken = default)
    {
        Result result = await sender.Send(
            new RecordOrderPickedUpCommand(
                integrationEvent.OrderId,
                integrationEvent.DeliveryId,
                integrationEvent.DriverId,
                integrationEvent.PickedUpOnUtc),
            cancellationToken);

        if (result.IsFailure)
        {
            throw new Common.Application.Exceptions.ApplicationException(
                nameof(RecordOrderPickedUpCommand),
                result.Error);
        }
    }
}

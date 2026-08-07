using FoodDeliveryService.Common.Application.EventBus;
using FoodDeliveryService.Common.Domain;
using FoodDeliveryService.Modules.Delivery.IntegrationEvents;
using FoodDeliveryService.Modules.FraudDetection.Application.Deliveries.RecordOrderDelivered;
using MediatR;

namespace FoodDeliveryService.Modules.FraudDetection.Presentation.Deliveries;

internal sealed class OrderDeliveredIntegrationEventHandler(ISender sender)
    : IntegrationEventHandler<OrderDeliveredIntegrationEvent>
{
    public override async Task Handle(
        OrderDeliveredIntegrationEvent integrationEvent,
        CancellationToken cancellationToken = default)
    {
        Result result = await sender.Send(
            new RecordOrderDeliveredCommand(
                integrationEvent.OrderId,
                integrationEvent.DeliveryId,
                integrationEvent.DriverId,
                integrationEvent.DeliveredOnUtc),
            cancellationToken);

        if (result.IsFailure)
        {
            throw new Common.Application.Exceptions.ApplicationException(
                nameof(RecordOrderDeliveredCommand),
                result.Error);
        }
    }
}

using FoodDeliveryService.Common.Application.EventBus;
using FoodDeliveryService.Common.Domain;
using FoodDeliveryService.Modules.FraudDetection.Application.Orders.RecordOrderAccepted;
using FoodDeliveryService.Modules.Orders.IntegrationEvents;
using MediatR;

namespace FoodDeliveryService.Modules.FraudDetection.Presentation.Orders;

internal sealed class OrderAcceptedIntegrationEventHandler(ISender sender)
    : IntegrationEventHandler<OrderAcceptedIntegrationEvent>
{
    public override async Task Handle(
        OrderAcceptedIntegrationEvent integrationEvent,
        CancellationToken cancellationToken = default)
    {
        Result result = await sender.Send(
            new RecordOrderAcceptedCommand(integrationEvent.OrderId, integrationEvent.AcceptedOnUtc),
            cancellationToken);

        if (result.IsFailure)
        {
            throw new Common.Application.Exceptions.ApplicationException(
                nameof(RecordOrderAcceptedCommand),
                result.Error);
        }
    }
}

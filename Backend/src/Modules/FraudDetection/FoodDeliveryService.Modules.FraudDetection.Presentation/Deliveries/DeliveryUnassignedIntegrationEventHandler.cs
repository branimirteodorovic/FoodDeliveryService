using FoodDeliveryService.Common.Application.EventBus;
using FoodDeliveryService.Common.Domain;
using FoodDeliveryService.Modules.Delivery.IntegrationEvents;
using FoodDeliveryService.Modules.FraudDetection.Application.Deliveries.RecordDeliveryUnassigned;
using MediatR;

namespace FoodDeliveryService.Modules.FraudDetection.Presentation.Deliveries;

internal sealed class DeliveryUnassignedIntegrationEventHandler(ISender sender)
    : IntegrationEventHandler<DeliveryUnassignedIntegrationEvent>
{
    public override async Task Handle(
        DeliveryUnassignedIntegrationEvent integrationEvent,
        CancellationToken cancellationToken = default)
    {
        Result result = await sender.Send(
            new RecordDeliveryUnassignedCommand(integrationEvent.OrderId, integrationEvent.OccurredOnUtc),
            cancellationToken);

        if (result.IsFailure)
        {
            throw new Common.Application.Exceptions.ApplicationException(
                nameof(RecordDeliveryUnassignedCommand),
                result.Error);
        }
    }
}

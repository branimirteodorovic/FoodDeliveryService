using FoodDeliveryService.Common.Application.EventBus;
using FoodDeliveryService.Common.Domain;
using FoodDeliveryService.Modules.Delivery.IntegrationEvents;
using FoodDeliveryService.Modules.FraudDetection.Application.Deliveries.RecordDeliveryOfferRejected;
using MediatR;

namespace FoodDeliveryService.Modules.FraudDetection.Presentation.Deliveries;

/// <summary>
/// The event carries no timestamp of its own, so the rejection is dated by its OccurredOnUtc — the
/// moment Delivery published it, which is the moment the driver declined.
/// </summary>
internal sealed class DeliveryOfferRejectedIntegrationEventHandler(ISender sender)
    : IntegrationEventHandler<DeliveryOfferRejectedIntegrationEvent>
{
    public override async Task Handle(
        DeliveryOfferRejectedIntegrationEvent integrationEvent,
        CancellationToken cancellationToken = default)
    {
        Result result = await sender.Send(
            new RecordDeliveryOfferRejectedCommand(integrationEvent.DriverId, integrationEvent.OccurredOnUtc),
            cancellationToken);

        if (result.IsFailure)
        {
            throw new Common.Application.Exceptions.ApplicationException(
                nameof(RecordDeliveryOfferRejectedCommand),
                result.Error);
        }
    }
}

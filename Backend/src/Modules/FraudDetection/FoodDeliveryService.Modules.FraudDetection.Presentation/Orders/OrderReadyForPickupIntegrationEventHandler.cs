using FoodDeliveryService.Common.Application.EventBus;
using FoodDeliveryService.Common.Domain;
using FoodDeliveryService.Modules.FraudDetection.Application.Orders.RecordOrderReadyForPickup;
using FoodDeliveryService.Modules.Orders.IntegrationEvents;
using MediatR;

namespace FoodDeliveryService.Modules.FraudDetection.Presentation.Orders;

/// <summary>
/// The only shipped event carrying the delivery coordinates, which is why FraudDetection consumes it — see
/// <see cref="RecordOrderReadyForPickupCommand"/>. Without it the drop-off columns on the fact
/// table would need an additive change to OrderPlaced upstream.
/// </summary>
internal sealed class OrderReadyForPickupIntegrationEventHandler(ISender sender)
    : IntegrationEventHandler<OrderReadyForPickupIntegrationEvent>
{
    public override async Task Handle(
        OrderReadyForPickupIntegrationEvent integrationEvent,
        CancellationToken cancellationToken = default)
    {
        Result result = await sender.Send(
            new RecordOrderReadyForPickupCommand(
                integrationEvent.OrderId,
                integrationEvent.CustomerId,
                integrationEvent.RestaurantId,
                integrationEvent.Subtotal,
                integrationEvent.PlacedOnUtc,
                integrationEvent.DeliveryLatitude,
                integrationEvent.DeliveryLongitude,
                integrationEvent.OccurredOnUtc),
            cancellationToken);

        if (result.IsFailure)
        {
            throw new Common.Application.Exceptions.ApplicationException(
                nameof(RecordOrderReadyForPickupCommand),
                result.Error);
        }
    }
}

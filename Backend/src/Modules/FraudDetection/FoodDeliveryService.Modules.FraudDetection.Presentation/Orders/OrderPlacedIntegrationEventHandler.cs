using FoodDeliveryService.Common.Application.EventBus;
using FoodDeliveryService.Common.Domain;
using FoodDeliveryService.Modules.FraudDetection.Application.Orders.RecordOrderPlaced;
using FoodDeliveryService.Modules.Orders.IntegrationEvents;
using MediatR;

namespace FoodDeliveryService.Modules.FraudDetection.Presentation.Orders;

internal sealed class OrderPlacedIntegrationEventHandler(ISender sender)
    : IntegrationEventHandler<OrderPlacedIntegrationEvent>
{
    public override async Task Handle(
        OrderPlacedIntegrationEvent integrationEvent,
        CancellationToken cancellationToken = default)
    {
        Result result = await sender.Send(
            new RecordOrderPlacedCommand(
                integrationEvent.OrderId,
                integrationEvent.CustomerId,
                integrationEvent.RestaurantId,
                integrationEvent.Subtotal,
                integrationEvent.PlacedOnUtc),
            cancellationToken);

        if (result.IsFailure)
        {
            throw new Common.Application.Exceptions.ApplicationException(
                nameof(RecordOrderPlacedCommand),
                result.Error);
        }
    }
}

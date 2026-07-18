using FoodDeliveryService.Common.Application.EventBus;
using FoodDeliveryService.Common.Domain;
using FoodDeliveryService.Modules.Delivery.Application.Deliveries.CancelDelivery;
using FoodDeliveryService.Modules.Orders.IntegrationEvents;
using MediatR;

namespace FoodDeliveryService.Modules.Delivery.Presentation.Orders;

/// <summary>
/// Compensation for an order cancelled mid-flight: cancels the delivery leg and releases a
/// reserved driver back to the pool. No timers to unwind — ProcessExpiredOffersJob simply stops
/// finding a Cancelled delivery.
/// </summary>
internal sealed class OrderCancelledIntegrationEventHandler(ISender sender)
    : IntegrationEventHandler<OrderCancelledIntegrationEvent>
{
    public override async Task Handle(
        OrderCancelledIntegrationEvent integrationEvent,
        CancellationToken cancellationToken = default)
    {
        Result result = await sender.Send(
            new CancelDeliveryCommand(integrationEvent.OrderId),
            cancellationToken);

        if (result.IsFailure)
        {
            throw new Common.Application.Exceptions.ApplicationException(
                nameof(CancelDeliveryCommand),
                result.Error);
        }
    }
}

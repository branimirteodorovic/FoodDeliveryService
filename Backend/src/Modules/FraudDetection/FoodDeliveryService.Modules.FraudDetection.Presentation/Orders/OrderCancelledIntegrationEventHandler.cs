using FoodDeliveryService.Common.Application.EventBus;
using FoodDeliveryService.Common.Domain;
using FoodDeliveryService.Modules.FraudDetection.Application.Orders.RecordOrderCancelled;
using FoodDeliveryService.Modules.Orders.IntegrationEvents;
using MediatR;

namespace FoodDeliveryService.Modules.FraudDetection.Presentation.Orders;

internal sealed class OrderCancelledIntegrationEventHandler(ISender sender)
    : IntegrationEventHandler<OrderCancelledIntegrationEvent>
{
    public override async Task Handle(
        OrderCancelledIntegrationEvent integrationEvent,
        CancellationToken cancellationToken = default)
    {
        Result result = await sender.Send(
            new RecordOrderCancelledCommand(
                integrationEvent.OrderId,
                integrationEvent.CustomerId,
                integrationEvent.CancelledOnUtc),
            cancellationToken);

        if (result.IsFailure)
        {
            throw new Common.Application.Exceptions.ApplicationException(
                nameof(RecordOrderCancelledCommand),
                result.Error);
        }
    }
}

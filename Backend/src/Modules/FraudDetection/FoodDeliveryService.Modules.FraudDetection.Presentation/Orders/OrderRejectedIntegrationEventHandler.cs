using FoodDeliveryService.Common.Application.EventBus;
using FoodDeliveryService.Common.Domain;
using FoodDeliveryService.Modules.FraudDetection.Application.Orders.RecordOrderRejected;
using FoodDeliveryService.Modules.Orders.IntegrationEvents;
using MediatR;

namespace FoodDeliveryService.Modules.FraudDetection.Presentation.Orders;

internal sealed class OrderRejectedIntegrationEventHandler(ISender sender)
    : IntegrationEventHandler<OrderRejectedIntegrationEvent>
{
    public override async Task Handle(
        OrderRejectedIntegrationEvent integrationEvent,
        CancellationToken cancellationToken = default)
    {
        Result result = await sender.Send(
            new RecordOrderRejectedCommand(
                integrationEvent.OrderId,
                integrationEvent.CustomerId,
                integrationEvent.RejectedOnUtc),
            cancellationToken);

        if (result.IsFailure)
        {
            throw new Common.Application.Exceptions.ApplicationException(
                nameof(RecordOrderRejectedCommand),
                result.Error);
        }
    }
}

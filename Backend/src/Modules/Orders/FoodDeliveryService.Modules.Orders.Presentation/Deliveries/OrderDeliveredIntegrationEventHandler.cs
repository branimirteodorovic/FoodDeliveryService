using FoodDeliveryService.Common.Application.EventBus;
using FoodDeliveryService.Common.Domain;
using FoodDeliveryService.Modules.Delivery.IntegrationEvents;
using FoodDeliveryService.Modules.Orders.Application.Orders.MarkOrderDelivered;
using MediatR;

namespace FoodDeliveryService.Modules.Orders.Presentation.Deliveries;

/// <summary>
/// The last transition, driven from the Delivery side: the driver delivered the order, so it
/// advances to Delivered. Dispatched by ProcessInboxJob (idempotent via the inbox). A failed
/// transition throws so the inbox retries rather than dropping it.
/// </summary>
internal sealed class OrderDeliveredIntegrationEventHandler(ISender sender)
    : IntegrationEventHandler<OrderDeliveredIntegrationEvent>
{
    public override async Task Handle(
        OrderDeliveredIntegrationEvent integrationEvent,
        CancellationToken cancellationToken = default)
    {
        Result result = await sender.Send(
            new MarkOrderDeliveredCommand(integrationEvent.OrderId, integrationEvent.DeliveredOnUtc),
            cancellationToken);

        if (result.IsFailure)
        {
            throw new Common.Application.Exceptions.ApplicationException(
                nameof(MarkOrderDeliveredCommand),
                result.Error);
        }
    }
}

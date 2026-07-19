using FoodDeliveryService.Common.Application.EventBus;
using FoodDeliveryService.Common.Domain;
using FoodDeliveryService.Modules.Delivery.IntegrationEvents;
using FoodDeliveryService.Modules.Orders.Application.Orders.MarkOrderOutForDelivery;
using MediatR;

namespace FoodDeliveryService.Modules.Orders.Presentation.Deliveries;

/// <summary>
/// Closes the loop from the Delivery side: the driver picked the order up, so the order advances to
/// OutForDelivery. Dispatched by ProcessInboxJob (idempotent via the inbox). A failed transition —
/// e.g. the order was cancelled concurrently — throws so the inbox retries rather than dropping it.
/// </summary>
internal sealed class OrderPickedUpIntegrationEventHandler(ISender sender)
    : IntegrationEventHandler<OrderPickedUpIntegrationEvent>
{
    public override async Task Handle(
        OrderPickedUpIntegrationEvent integrationEvent,
        CancellationToken cancellationToken = default)
    {
        Result result = await sender.Send(
            new MarkOrderOutForDeliveryCommand(integrationEvent.OrderId),
            cancellationToken);

        if (result.IsFailure)
        {
            throw new Common.Application.Exceptions.ApplicationException(
                nameof(MarkOrderOutForDeliveryCommand),
                result.Error);
        }
    }
}

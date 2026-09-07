using FoodDeliveryService.Common.Application.EventBus;
using FoodDeliveryService.Common.Domain;
using FoodDeliveryService.Modules.Notifications.Application.Notifications.SendOrderConfirmation;
using FoodDeliveryService.Modules.Orders.IntegrationEvents;
using MediatR;

namespace FoodDeliveryService.Modules.Notifications.Presentation.Notifications;

/// <summary>
/// Sends the customer their order-confirmation email when an order is placed (dispatched by
/// ProcessInboxJob, idempotent via the inbox — a duplicate delivery of the same event never
/// produces a second email). A missing recipient replica throws, which leaves the failure on the
/// inbox row's error column rather than dropping it silently. The message is still marked
/// processed either way: ProcessInboxJob does not retry a failed handler.
/// </summary>
internal sealed class OrderPlacedIntegrationEventHandler(ISender sender)
    : IntegrationEventHandler<OrderPlacedIntegrationEvent>
{
    public override async Task Handle(
        OrderPlacedIntegrationEvent integrationEvent,
        CancellationToken cancellationToken = default)
    {
        Result result = await sender.Send(
            new SendOrderConfirmationCommand(
                integrationEvent.CustomerId,
                integrationEvent.OrderId,
                integrationEvent.Subtotal),
            cancellationToken);

        if (result.IsFailure)
        {
            throw new Common.Application.Exceptions.ApplicationException(
                nameof(SendOrderConfirmationCommand),
                result.Error);
        }
    }
}

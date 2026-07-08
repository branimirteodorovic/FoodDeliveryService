using FoodDeliveryService.Common.Application.EventBus;
using FoodDeliveryService.Common.Domain;
using FoodDeliveryService.Modules.Notifications.Application.Notifications.SendOrderConfirmation;
using FoodDeliveryService.Modules.Orders.IntegrationEvents;
using MediatR;

namespace FoodDeliveryService.Modules.Notifications.Presentation.Notifications;

/// <summary>
/// Sends the customer their order-confirmation email when an order is placed (dispatched by
/// ProcessInboxJob, idempotent via the inbox — a duplicate delivery of the same event never
/// produces a second email). A missing recipient replica throws so the inbox retries and the
/// failure stays visible on the inbox row rather than being silently dropped.
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

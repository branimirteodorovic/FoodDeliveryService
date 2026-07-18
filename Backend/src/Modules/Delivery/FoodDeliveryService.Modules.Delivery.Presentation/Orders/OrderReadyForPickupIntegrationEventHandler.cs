using FoodDeliveryService.Common.Application.EventBus;
using FoodDeliveryService.Common.Domain;
using FoodDeliveryService.Modules.Delivery.Application.Orders.UpsertOrder;
using FoodDeliveryService.Modules.Orders.IntegrationEvents;
using MediatR;

namespace FoodDeliveryService.Modules.Delivery.Presentation.Orders;

/// <summary>
/// Creates/refreshes the local Order replica when an order is ready for pickup (dispatched by
/// ProcessInboxJob, idempotent via the inbox). Milestone E extends this handler to also create the
/// Delivery aggregate and run the offer routine; for now it only lands the replica.
/// </summary>
internal sealed class OrderReadyForPickupIntegrationEventHandler(ISender sender)
    : IntegrationEventHandler<OrderReadyForPickupIntegrationEvent>
{
    public override async Task Handle(
        OrderReadyForPickupIntegrationEvent integrationEvent,
        CancellationToken cancellationToken = default)
    {
        Result result = await sender.Send(
            new UpsertOrderCommand(
                integrationEvent.OrderId,
                integrationEvent.CustomerId,
                integrationEvent.RestaurantId,
                integrationEvent.DeliveryStreet,
                integrationEvent.DeliveryCity,
                integrationEvent.DeliveryPostalCode,
                integrationEvent.DeliveryCountry,
                integrationEvent.DeliveryNotes,
                integrationEvent.DeliveryLatitude,
                integrationEvent.DeliveryLongitude,
                integrationEvent.PlacedOnUtc),
            cancellationToken);

        if (result.IsFailure)
        {
            throw new Common.Application.Exceptions.ApplicationException(
                nameof(UpsertOrderCommand),
                result.Error);
        }
    }
}

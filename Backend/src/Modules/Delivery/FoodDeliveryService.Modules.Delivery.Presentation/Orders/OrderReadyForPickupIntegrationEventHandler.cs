using FoodDeliveryService.Common.Application.EventBus;
using FoodDeliveryService.Common.Domain;
using FoodDeliveryService.Modules.Delivery.Application.Deliveries.CreateDelivery;
using FoodDeliveryService.Modules.Delivery.Application.Orders.UpsertOrder;
using FoodDeliveryService.Modules.Orders.IntegrationEvents;
using MediatR;

namespace FoodDeliveryService.Modules.Delivery.Presentation.Orders;

/// <summary>
/// Creates/refreshes the local Order replica when an order is ready for pickup, then creates the
/// Delivery aggregate and starts the offer routine (dispatched by ProcessInboxJob; idempotent via
/// the inbox, the replica upsert, and the delivery's unique OrderId).
/// </summary>
internal sealed class OrderReadyForPickupIntegrationEventHandler(ISender sender)
    : IntegrationEventHandler<OrderReadyForPickupIntegrationEvent>
{
    public override async Task Handle(
        OrderReadyForPickupIntegrationEvent integrationEvent,
        CancellationToken cancellationToken = default)
    {
        Result upsertResult = await sender.Send(
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

        if (upsertResult.IsFailure)
        {
            throw new Common.Application.Exceptions.ApplicationException(
                nameof(UpsertOrderCommand),
                upsertResult.Error);
        }

        // The initial offer uses the coordinates carried on the event; the aggregate snapshots the
        // pickup location so later re-offers don't depend on the event still being around.
        Result createResult = await sender.Send(
            new CreateDeliveryCommand(
                integrationEvent.OrderId,
                integrationEvent.RestaurantId,
                integrationEvent.CustomerId,
                integrationEvent.RestaurantLatitude,
                integrationEvent.RestaurantLongitude,
                integrationEvent.DeliveryStreet,
                integrationEvent.DeliveryCity,
                integrationEvent.DeliveryPostalCode,
                integrationEvent.DeliveryCountry,
                integrationEvent.DeliveryNotes,
                integrationEvent.DeliveryLatitude,
                integrationEvent.DeliveryLongitude),
            cancellationToken);

        if (createResult.IsFailure)
        {
            throw new Common.Application.Exceptions.ApplicationException(
                nameof(CreateDeliveryCommand),
                createResult.Error);
        }
    }
}

using FoodDeliveryService.Common.Application.EventBus;
using FoodDeliveryService.Common.Application.Messaging;
using FoodDeliveryService.Common.Domain;
using FoodDeliveryService.Modules.Orders.Application.Diagnostics;
using FoodDeliveryService.Modules.Orders.Domain.Orders;
using FoodDeliveryService.Modules.Orders.IntegrationEvents;
using MediatR;

namespace FoodDeliveryService.Modules.Orders.Application.Orders.MarkOrderReady;

// This is the event the Delivery service (Phase 2) consumes to start driver assignment. It carries
// a full snapshot — pickup + dropoff coordinates and the subtotal — so Delivery never calls back.
internal sealed class OrderReadyForPickupDomainEventHandler(ISender sender, IEventBus eventBus)
    : DomainEventHandler<OrderReadyForPickupDomainEvent>
{
    public override async Task Handle(
        OrderReadyForPickupDomainEvent domainEvent,
        CancellationToken cancellationToken = default)
    {
        Result<OrderPickupDetailsResponse> result = await sender.Send(
            new GetOrderPickupDetailsQuery(domainEvent.OrderId),
            cancellationToken);

        if (result.IsFailure)
        {
            throw new Common.Application.Exceptions.ApplicationException(
                nameof(GetOrderPickupDetailsQuery),
                result.Error);
        }

        OrderPickupDetailsResponse details = result.Value;

        await eventBus.PublishAsync(
            new OrderReadyForPickupIntegrationEvent(
                domainEvent.Id,
                domainEvent.OccurredOnUtc,
                details.OrderId,
                details.CustomerId,
                details.RestaurantId,
                details.RestaurantLatitude,
                details.RestaurantLongitude,
                details.DeliveryStreet,
                details.DeliveryCity,
                details.DeliveryPostalCode,
                details.DeliveryCountry,
                details.DeliveryNotes,
                details.DeliveryLatitude,
                details.DeliveryLongitude,
                details.Subtotal,
                details.PlacedOnUtc),
            cancellationToken);

        // Last, and it matters most here: this handler throws when the pickup-details query fails,
        // and the outbox re-runs the whole handler on the next tick — see
        // OrderPlacedDomainEventHandler.
        OrdersDiagnostics.RecordTransition(domainEvent.PreviousStatus, OrderStatus.ReadyForPickup);
    }
}

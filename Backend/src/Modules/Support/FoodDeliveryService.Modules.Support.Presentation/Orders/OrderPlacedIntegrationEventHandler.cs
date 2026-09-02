using FoodDeliveryService.Common.Application.EventBus;
using FoodDeliveryService.Common.Domain;
using FoodDeliveryService.Modules.Orders.IntegrationEvents;
using FoodDeliveryService.Modules.Support.Application.Orders.UpsertOrderSnapshot;
using MediatR;

namespace FoodDeliveryService.Modules.Support.Presentation.Orders;

/// <summary>
/// Builds Support's local order replica (dispatched by ProcessInboxJob, idempotent via the inbox).
/// <para>
/// Every order on the platform reaches this handler and every one is kept — unlike the agent
/// replica, there is no staff filter here, because any order can become the subject of a ticket and
/// the event is the only chance this service gets to learn its subtotal. Hard rule #5 forbids
/// asking Orders later.
/// </para>
/// <para>
/// One event of the eight lifecycle events today. The refund ceiling is the only fact about an
/// order the service currently needs, and <c>OrderPlaced</c> is the one carrying the subtotal; the
/// remaining seven — including the two whose states come from <em>Delivery</em>, since Orders
/// publishes no <c>OutForDelivery</c> or <c>Delivered</c> event of its own — arrive with the ticket
/// context milestone as sibling handlers in this folder.
/// </para>
/// </summary>
internal sealed class OrderPlacedIntegrationEventHandler(ISender sender)
    : IntegrationEventHandler<OrderPlacedIntegrationEvent>
{
    public override async Task Handle(
        OrderPlacedIntegrationEvent integrationEvent,
        CancellationToken cancellationToken = default)
    {
        Result result = await sender.Send(
            new UpsertOrderSnapshotCommand(
                integrationEvent.OrderId,
                integrationEvent.CustomerId,
                integrationEvent.RestaurantId,
                integrationEvent.Subtotal,
                integrationEvent.PlacedOnUtc,

                // The event's own timestamp, not the clock: the projection needs to be able to tell
                // a late redelivery from a newer fact once the other seven events land.
                integrationEvent.OccurredOnUtc),
            cancellationToken);

        if (result.IsFailure)
        {
            throw new Common.Application.Exceptions.ApplicationException(
                nameof(UpsertOrderSnapshotCommand),
                result.Error);
        }
    }
}

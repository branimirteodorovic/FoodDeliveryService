using FoodDeliveryService.Modules.RealTime.Application.RealTime;
using MassTransit;
using Microsoft.Extensions.Logging;

namespace FoodDeliveryService.Modules.RealTime.Infrastructure.Consumers;

/// <summary>
/// Shared body for the Milestone C Delivery lifecycle consumers. Unlike the Orders events
/// (<see cref="OrderStatusConsumer{TEvent}"/>), a Delivery event carries no <c>CustomerId</c> of its
/// own, so the customer is resolved from the <see cref="IOrderRoutingMap"/> row Milestone B already
/// warmed for the order. Same deliberate departure from the inbox rule as the Orders consumers — see
/// that class's XML doc for the justification; it applies identically here.
/// </summary>
internal abstract class DeliveryStatusConsumer<TEvent>(
    IRealTimeNotifier notifier,
    IOrderRoutingMap routingMap,
    ILogger logger) : IConsumer<TEvent>
    where TEvent : class
{
    protected abstract Guid GetOrderId(TEvent message);

    protected abstract OrderStatusFrame MapFrame(TEvent message);

    /// <summary>Extension point for binding/unbinding the driver for the location subscriber — run
    /// <b>before</b> the frame is sent (despite the name, kept for parity with
    /// <c>OrderStatusConsumer</c>'s hook) so the binding is already correct if the client reacts to
    /// the frame, or a location PUBLISH races in immediately after. No-op by default.</summary>
    protected virtual Task AfterFanOutAsync(TEvent message, Guid customerId, CancellationToken cancellationToken) =>
        Task.CompletedTask;

    public async Task Consume(ConsumeContext<TEvent> context)
    {
        Guid orderId = GetOrderId(context.Message);

        OrderRoutingEntry? entry = await routingMap.GetAsync(orderId, context.CancellationToken);

        if (entry is null)
        {
            // Best-effort: the client re-syncs authoritative state from the REST read models on
            // (re)connect, so a dropped frame here is never a correctness problem — just log it.
            logger.LogWarning(
                "No routing entry for order {OrderId}; dropping {Event} frame",
                orderId, typeof(TEvent).Name);
            return;
        }

        // Bind/unbind before the frame goes out (same ordering as OrderStatusConsumer warming the
        // routing map first): a client that reacts to the frame by expecting position updates (or
        // that races a location PUBLISH landing immediately after) must see up-to-date binding state.
        await AfterFanOutAsync(context.Message, entry.CustomerId, context.CancellationToken);

        OrderStatusFrame frame = MapFrame(context.Message);

        await notifier.NotifyUserAsync(entry.CustomerId, frame, context.CancellationToken);

        // Milestone D: same dashboard/support fan-out as the Orders-owned transitions, resolved via
        // the routing entry's RestaurantId (Delivery events carry no RestaurantId of their own).
        await notifier.NotifyRestaurantAsync(
            entry.RestaurantId,
            new RestaurantActivityFrame(frame.OrderId, frame.Status, frame.OccurredOnUtc),
            context.CancellationToken);

        await notifier.NotifySupportAsync(
            new SupportActivityFrame(frame.OrderId, entry.RestaurantId, frame.Status, frame.OccurredOnUtc),
            context.CancellationToken);
    }
}

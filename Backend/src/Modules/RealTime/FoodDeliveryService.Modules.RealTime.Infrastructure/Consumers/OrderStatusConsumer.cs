using FoodDeliveryService.Modules.RealTime.Application.RealTime;
using MassTransit;

namespace FoodDeliveryService.Modules.RealTime.Infrastructure.Consumers;

/// <summary>
/// Shared body for the Orders lifecycle consumers: map the event to a frame, warm the routing map,
/// fan the frame out to the customer's group. Each concrete subclass binds one event type and
/// supplies only the per-event mapping.
/// <para>
/// <b>Deliberate departure from the inbox rule.</b> Unlike every other module, these are direct
/// <see cref="IConsumer{T}"/> implementations that broadcast immediately — they do <b>not</b> write
/// an <c>inbox_messages</c> row. Durability belongs to the other services' databases and the REST
/// read models the client re-syncs from on (re)connect, not to a transient socket frame; routing a
/// "real-time" push through the inbox's Quartz poll would only add latency. This exception is scoped
/// to the RealTime service — do not "fix" it by switching to <c>IntegrationEventConsumer&lt;T&gt;</c>.
/// </para>
/// </summary>
internal abstract class OrderStatusConsumer<TEvent>(
    IRealTimeNotifier notifier,
    IOrderRoutingMap routingMap) : IConsumer<TEvent>
    where TEvent : class
{
    /// <summary>Maps the specific lifecycle event to the frame plus the routing ids every Orders
    /// event carries (there is no shared contract interface, so each subclass pulls them out).</summary>
    protected abstract (OrderStatusFrame Frame, Guid CustomerId, Guid RestaurantId) Map(TEvent message);

    /// <summary>Extension point for a subclass that needs to react to this transition — e.g.
    /// <c>OrderCancelledConsumer</c> clearing a Milestone C driver binding. Run <b>before</b> the
    /// frame is sent (despite the name) so a client reacting to the frame, or a location PUBLISH
    /// racing in immediately after, always sees up-to-date binding state. No-op by default.</summary>
    protected virtual Task AfterFanOutAsync(TEvent message, CancellationToken cancellationToken) => Task.CompletedTask;

    public async Task Consume(ConsumeContext<TEvent> context)
    {
        (OrderStatusFrame frame, Guid customerId, Guid restaurantId) = Map(context.Message);

        // Warm the routing map on every transition so a driver-location frame (Milestone C) can
        // resolve this order's customer even before a driver is assigned.
        await routingMap.SetAsync(
            frame.OrderId,
            new OrderRoutingEntry(customerId, restaurantId),
            context.CancellationToken);

        await AfterFanOutAsync(context.Message, context.CancellationToken);

        await notifier.NotifyUserAsync(customerId, frame, context.CancellationToken);
    }
}

using FoodDeliveryService.Modules.RealTime.Application.RealTime;
using FoodDeliveryService.Modules.RealTime.Presentation.Tracking;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;

namespace FoodDeliveryService.Modules.RealTime.Infrastructure.RealTime;

/// <summary>
/// Fans a frame out to a single customer's <c>user:{userId}</c> group over the SignalR hub (backed by
/// the Redis backplane, so it reaches a connection held by any RealTime instance). This is the one
/// place the fan-out path lives, so it is also where the OrderId is pushed into the log context — the
/// SignalR send is not auto-instrumented, and a "stuck timeline" needs to be debuggable.
/// <para>
/// Best-effort by contract: a failed send is swallowed and logged, never rethrown. Throwing would
/// fault the consuming bus message and trigger a pointless retry for a transient socket frame the
/// client will re-derive from the REST read models on its next (re)connect anyway.
/// </para>
/// </summary>
internal sealed class RealTimeNotifier(
    IHubContext<TrackingHub> hubContext,
    ILogger<RealTimeNotifier> logger) : IRealTimeNotifier
{
    public async Task NotifyUserAsync(Guid userId, OrderStatusFrame frame, CancellationToken cancellationToken = default)
    {
        using IDisposable? logScope = logger.BeginScope(new Dictionary<string, object>
        {
            ["OrderId"] = frame.OrderId,
            ["Status"] = frame.Status
        });

        try
        {
            await hubContext.Clients
                .Group(GroupNames.User(userId))
                .SendAsync(TrackingHubMethods.OrderStatusChanged, frame, cancellationToken);

            logger.LogDebug("Pushed order-status frame '{Status}' for order {OrderId} to {Group}",
                frame.Status, frame.OrderId, GroupNames.User(userId));
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // Ephemeral frame — never fault the bus message over a dropped socket send.
            logger.LogWarning(exception,
                "Failed to push order-status frame '{Status}' for order {OrderId} to {Group}",
                frame.Status, frame.OrderId, GroupNames.User(userId));
        }
    }

    public async Task NotifyUserAsync(Guid userId, DriverLocationFrame frame, CancellationToken cancellationToken = default)
    {
        using IDisposable? logScope = logger.BeginScope(new Dictionary<string, object>
        {
            ["OrderId"] = frame.OrderId,
            ["DriverId"] = frame.DriverId
        });

        try
        {
            await hubContext.Clients
                .Group(GroupNames.User(userId))
                .SendAsync(TrackingHubMethods.DriverLocationChanged, frame, cancellationToken);

            logger.LogDebug("Pushed driver-location frame for order {OrderId} to {Group}",
                frame.OrderId, GroupNames.User(userId));
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // Ephemeral frame — a stuck/dropped position is never worth retrying.
            logger.LogWarning(exception,
                "Failed to push driver-location frame for order {OrderId} to {Group}",
                frame.OrderId, GroupNames.User(userId));
        }
    }

    public async Task NotifyRestaurantAsync(Guid restaurantId, RestaurantActivityFrame frame, CancellationToken cancellationToken = default)
    {
        using IDisposable? logScope = logger.BeginScope(new Dictionary<string, object>
        {
            ["OrderId"] = frame.OrderId,
            ["RestaurantId"] = restaurantId,
            ["Status"] = frame.Status
        });

        try
        {
            await hubContext.Clients
                .Group(GroupNames.Restaurant(restaurantId))
                .SendAsync(TrackingHubMethods.RestaurantActivity, frame, cancellationToken);

            logger.LogDebug("Pushed restaurant-activity frame '{Status}' for order {OrderId} to {Group}",
                frame.Status, frame.OrderId, GroupNames.Restaurant(restaurantId));
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning(exception,
                "Failed to push restaurant-activity frame '{Status}' for order {OrderId} to {Group}",
                frame.Status, frame.OrderId, GroupNames.Restaurant(restaurantId));
        }
    }

    public async Task NotifySupportAsync(SupportActivityFrame frame, CancellationToken cancellationToken = default)
    {
        using IDisposable? logScope = logger.BeginScope(new Dictionary<string, object>
        {
            ["OrderId"] = frame.OrderId,
            ["RestaurantId"] = frame.RestaurantId,
            ["Status"] = frame.Status
        });

        try
        {
            await hubContext.Clients
                .Group(GroupNames.Support)
                .SendAsync(TrackingHubMethods.SupportActivity, frame, cancellationToken);

            logger.LogDebug("Pushed support-activity frame '{Status}' for order {OrderId} to {Group}",
                frame.Status, frame.OrderId, GroupNames.Support);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning(exception,
                "Failed to push support-activity frame '{Status}' for order {OrderId} to {Group}",
                frame.Status, frame.OrderId, GroupNames.Support);
        }
    }
}

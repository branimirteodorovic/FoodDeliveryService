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
}

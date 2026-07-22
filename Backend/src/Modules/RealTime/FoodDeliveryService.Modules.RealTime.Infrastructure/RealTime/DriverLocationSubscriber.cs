using System.Diagnostics;
using System.Text.Json;
using FoodDeliveryService.Modules.RealTime.Application.RealTime;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace FoodDeliveryService.Modules.RealTime.Infrastructure.RealTime;

/// <summary>
/// Subscribes to Delivery's <c>delivery:driver-locations</c> Redis pub/sub channel (plan §4.1/§4.3)
/// and forwards each position to the customer currently tracking that driver's order. Deliberately
/// off the bus — this is the highest-frequency stream in the system, and neither side needs a
/// durability guarantee for a single stale position: the client's next frame (or its own reconnect
/// re-sync) supersedes it.
/// <para>
/// A driver with no active <see cref="IDriverBindingStore"/> binding (never assigned, or the order
/// already delivered/cancelled) produces a silently dropped frame — that binding is exactly the
/// signal for "nobody should receive this position anymore".
/// </para>
/// </summary>
internal sealed class DriverLocationSubscriber(
    IConnectionMultiplexer connectionMultiplexer,
    IDriverBindingStore driverBindingStore,
    IRealTimeNotifier notifier,
    ILogger<DriverLocationSubscriber> logger) : BackgroundService
{
    private const string Channel = "delivery:driver-locations";

    private ChannelMessageQueue? _queue;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        ISubscriber subscriber = connectionMultiplexer.GetSubscriber();

        _queue = await subscriber.SubscribeAsync(RedisChannel.Literal(Channel));
        _queue.OnMessage(channelMessage => HandleMessageAsync(channelMessage.Message, stoppingToken));
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_queue is not null)
        {
            await _queue.UnsubscribeAsync();
        }

        await base.StopAsync(cancellationToken);
    }

    private async Task HandleMessageAsync(RedisValue message, CancellationToken cancellationToken)
    {
        using Activity? activity = RealTimeDiagnostics.ActivitySource.StartActivity("ForwardDriverLocation");

        try
        {
            DriverLocationPublishedMessage? payload = JsonSerializer.Deserialize<DriverLocationPublishedMessage>((string)message!);

            if (payload is null)
            {
                return;
            }

            activity?.SetTag("delivery.driver_id", payload.DriverId);

            DriverBinding? binding = await driverBindingStore.GetAsync(payload.DriverId, cancellationToken);

            if (binding is null)
            {
                // Unassigned/finished driver — no active tracker, drop the frame.
                return;
            }

            activity?.SetTag("realtime.order_id", binding.OrderId);

            await notifier.NotifyUserAsync(
                binding.CustomerId,
                new DriverLocationFrame(binding.OrderId, payload.DriverId, payload.Latitude, payload.Longitude, payload.RecordedOnUtc),
                cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // Best-effort stream — never let a single malformed/failed frame take the subscriber down.
            logger.LogWarning(exception, "Failed to forward driver-location message");
        }
    }
}

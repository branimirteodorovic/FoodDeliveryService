using Serilog.Core;
using Serilog.Events;

namespace FoodDeliveryService.Modules.Orders.IntegrationTests.Abstractions;

/// <summary>
/// Holds the host's own log events so a test can assert what the Serilog <c>LogContext</c> carried
/// while the request ran. There is no way to attach a sink to a host after it is built — Serilog's
/// logger is created once, inside the <c>ILoggerFactory</c> registration — so
/// <see cref="IntegrationTestWebAppFactory"/> replaces that registration with a logger writing here.
/// </summary>
internal sealed class CapturingLogSink : ILogEventSink
{
    /// <summary>
    /// Bounded because the host logs continuously: EF Core, the Quartz outbox/inbox jobs and
    /// MassTransit all write between tests. Assertions run immediately after the request that
    /// produced the events they look for, so a few thousand lines of headroom is plenty and an
    /// unbounded list would just grow for the whole run.
    /// </summary>
    private const int Capacity = 2000;

    private readonly Queue<LogEvent> _events = new();
    private readonly Lock _gate = new();

    public void Emit(LogEvent logEvent)
    {
        lock (_gate)
        {
            _events.Enqueue(logEvent);

            if (_events.Count > Capacity)
            {
                _events.Dequeue();
            }
        }
    }

    public IReadOnlyList<LogEvent> Snapshot()
    {
        lock (_gate)
        {
            return [.. _events];
        }
    }
}

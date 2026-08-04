using System.Diagnostics;

namespace FoodDeliveryService.Common.Presentation.Correlation;

/// <summary>
/// The correlation id and trace context of whatever is currently executing, readable from code that
/// has no <see cref="Microsoft.AspNetCore.Http.HttpContext"/> — the outbox interceptor, the
/// MassTransit publish filter, the Quartz dispatch jobs. It is what carries correlation across the
/// two <b>database</b> handoffs (<c>outbox_messages</c> and <c>inbox_messages</c>), where the
/// request that caused the work is long gone by the time the work runs.
/// <para>
/// <b>Why the state is ambient (an <see cref="AsyncLocal{T}"/>) rather than a DI scope.</b> The two
/// things this sits between — <see cref="Activity.Current"/> and Serilog's <c>LogContext</c> — are
/// both ambient, and so are the call sites: <c>IEventBus</c> is a singleton over MassTransit's
/// <c>IBus</c>, so a publish filter runs in a DI scope of MassTransit's own making with no
/// relationship to the scope the publishing handler was resolved from. A scoped service would read
/// back empty there, which is precisely the leg this milestone exists to cover.
/// </para>
/// <para>
/// The slot is an instance field, not a static, and the type is registered as a singleton — so the
/// ambient value belongs to <i>a host</i>. That matters where two hosts share a process (the
/// integration tests run Users alongside Orders): each stamps its own outbox and inbox rows from its
/// own context, exactly as two containers would.
/// </para>
/// </summary>
public sealed class CorrelationContext
{
    private readonly AsyncLocal<CorrelationValues?> _ambient = new();

    /// <summary>
    /// The id every log line of the current unit of work should carry. Falls back to the ambient
    /// trace id for work that <i>originates</i> in a background job — nothing pushed a value there,
    /// and a job-initiated flow deserves an id rather than a null column.
    /// </summary>
    public string? CorrelationId => _ambient.Value?.CorrelationId ?? Activity.Current?.TraceId.ToString();

    /// <summary>
    /// The W3C <c>traceparent</c> of the span that caused the current work, in the form
    /// <c>00-{trace-id}-{span-id}-{flags}</c> — stored on the outbox/inbox row so a dispatch that
    /// happens seconds later can point back at the trace that produced the message.
    /// </summary>
    public string? TraceParent => _ambient.Value?.TraceParent ?? Activity.Current?.Id;

    /// <summary>
    /// Makes the given context current until the returned handle is disposed, restoring whatever was
    /// current before. Modelled on <c>LogContext.Push</c> deliberately: the two are pushed together
    /// and must unwind together.
    /// </summary>
    public IDisposable Push(string? correlationId, string? traceParent)
    {
        CorrelationValues? previous = _ambient.Value;

        _ambient.Value = new CorrelationValues(correlationId, traceParent);

        return new AmbientScope(_ambient, previous);
    }

    private sealed record CorrelationValues(string? CorrelationId, string? TraceParent);

    private sealed class AmbientScope(AsyncLocal<CorrelationValues?> ambient, CorrelationValues? previous)
        : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;

            ambient.Value = previous;
        }
    }
}

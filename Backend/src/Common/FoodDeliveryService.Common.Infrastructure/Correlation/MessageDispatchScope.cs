using System.Diagnostics;
using FoodDeliveryService.Common.Presentation.Correlation;
using Serilog.Context;
using Serilog.Core;
using Serilog.Core.Enrichers;

namespace FoodDeliveryService.Common.Infrastructure.Correlation;

/// <summary>
/// Restores the correlation of a message that has been sitting in a database table. Every
/// <c>ProcessOutboxJob</c> and <c>ProcessInboxJob</c> opens one of these around each message it
/// dispatches, and for the life of that scope the work looks correlated to everything downstream:
/// the Serilog <c>LogContext</c> carries the id, the ambient <see cref="CorrelationContext"/> carries
/// it onward (onto the next outbox row, onto the bus header), and a dispatch span points back at the
/// trace that produced the message.
/// <para>
/// <b>One helper, not eleven copies.</b> The dispatch jobs are copy-pasted per module — five outbox,
/// six inbox — which is exactly the duplication Milestone D removed from the host middleware. The
/// jobs call this; they do not each own a version of it.
/// </para>
/// <para>
/// <b>Link, not parent — and what that trades.</b> The dispatch activity carries the stored trace
/// context as an <see cref="ActivityLink"/> and starts a new trace of its own. A job execution
/// drains a batch of messages belonging to N different requests, so no single one of them can
/// honestly be its parent; and a true continuation makes traces unbounded in time — a message that
/// fails and retries for an hour would keep appending to a trace whose root span "lasted" an hour.
/// The correlation <i>id</i> in the logs is what answers "show me everything about this order" in
/// one query; the link is what lets a trace UI navigate between the two traces. Making the dispatch
/// a genuine child instead is a one-line change here (pass the parsed context as
/// <c>parentContext</c> rather than as a link) — the two look nearly identical in code and very
/// different in Jaeger.
/// </para>
/// </summary>
public static class MessageDispatchScope
{
    /// <summary>
    /// Opens the scope. Dispose it when the message is done — before the next message in the batch,
    /// so two messages never share one scope.
    /// <para>
    /// It is deliberately opened from the row alone, <b>before</b> the content is deserialized, so
    /// that the "exception while processing message" line — the one line anybody actually goes
    /// looking for — is written inside the scope rather than just outside it. The business ids come
    /// afterwards, from <see cref="PushBusinessIds"/>, because they need the deserialized event.
    /// </para>
    /// </summary>
    /// <param name="correlationContext">The ambient context to re-seed for the duration.</param>
    /// <param name="operation">
    /// <see cref="MessagingDiagnostics.OutboxDispatch"/> or
    /// <see cref="MessagingDiagnostics.InboxDispatch"/>.
    /// </param>
    /// <param name="messageType">The row's <c>type</c> column, which names the span.</param>
    /// <param name="correlationId">
    /// The <c>correlation_id</c> column. Null for a row written before this milestone, or for work
    /// that originated in a job — the dispatch activity's own trace id stands in.
    /// </param>
    /// <param name="traceParent">
    /// The <c>trace_parent</c> column. Null or malformed simply produces no link: a pre-migration
    /// row must still dispatch, and nothing here may throw inside a job loop.
    /// </param>
    public static IDisposable Begin(
        CorrelationContext correlationContext,
        string operation,
        string messageType,
        string? correlationId,
        string? traceParent)
    {
        ArgumentNullException.ThrowIfNull(correlationContext);

        // Pushed before the activity starts so that anything reading the ambient context during
        // dispatch — the outbox interceptor on a nested SaveChanges, the publish filter — sees the
        // ORIGINATING context rather than this dispatch's, keeping a chain of messages pointed at
        // the request that started it instead of at the previous hop.
        IDisposable ambient = correlationContext.Push(correlationId, traceParent);

        // A dispatch is a new trace, not a continuation of whatever the job loop left current — the
        // Npgsql span of the SELECT that read the batch, most often. ActivitySource reads a default
        // ActivityContext as "no explicit parent given" and falls back to Activity.Current, so
        // clearing it is the only way to ask for a root span.
        Activity? previousActivity = Activity.Current;

        Activity.Current = null;

        Activity? activity = StartActivity(operation, messageType, traceParent);

        if (activity is null)
        {
            // Nothing is listening to the source. Put back what was current rather than running the
            // handlers under a cleared one.
            Activity.Current = previousActivity;
        }

        // Read back rather than reusing the argument: when the column was null, the ambient context
        // has just fallen back to the activity started above, so a job-initiated flow gets a real id
        // in its logs instead of an absent property.
        string? effectiveCorrelationId = correlationContext.CorrelationId;

        activity?.SetTag("correlation.id", effectiveCorrelationId);

        IDisposable logScope = LogContext.Push(BuildEnrichers(activity, effectiveCorrelationId));

        return new DispatchScope(logScope, activity, ambient, previousActivity);
    }

    /// <summary>
    /// Adds the message's own business ids — <c>OrderId</c>, <c>DeliveryId</c>, <c>UserId</c> — to
    /// the log scope for the rest of the dispatch. This is what makes <c>OrderId = '…'</c> in Seq
    /// return the placement request <i>and</i> the outbox dispatch <i>and</i> the consuming handler,
    /// which is what "search for all logs related to one order" actually means.
    /// </summary>
    public static IDisposable PushBusinessIds(object message)
    {
        ArgumentNullException.ThrowIfNull(message);

        ILogEventEnricher[] enrichers =
        [
            .. MessageBusinessIds
                .Extract(message)
                .Select(businessId => new PropertyEnricher(businessId.Key, businessId.Value))
        ];

        return LogContext.Push(enrichers);
    }

    private static Activity? StartActivity(string operation, string messageType, string? traceParent)
    {
        // TryParse returns false for null, for a truncated value and for anything that is not a W3C
        // traceparent — all of which degrade to "no link", never to an exception.
        ActivityLink[]? links = ActivityContext.TryParse(traceParent, traceState: null, out ActivityContext origin)
            ? [new ActivityLink(origin)]
            : null;

        // parentContext: default is what makes this a root span. Passing the ActivityContext
        // overload at all is deliberate — the overloads that take no parent fall back to
        // Activity.Current, which inside a Quartz job is whatever the previous message left behind.
        return MessagingDiagnostics.ActivitySource.StartActivity(
            $"{operation} {messageType}",
            ActivityKind.Consumer,
            parentContext: default,
            tags: null,
            links: links);
    }

    private static ILogEventEnricher[] BuildEnrichers(Activity? activity, string? correlationId)
    {
        List<ILogEventEnricher> enrichers = [];

        if (!string.IsNullOrEmpty(correlationId))
        {
            enrichers.Add(new PropertyEnricher("CorrelationId", correlationId));
        }

        // The same two properties the HTTP path pushes, so a Seq line from a job is one click from
        // its trace exactly like a line from a request.
        if (activity is not null)
        {
            enrichers.Add(new PropertyEnricher("TraceId", activity.TraceId.ToString()));
            enrichers.Add(new PropertyEnricher("SpanId", activity.SpanId.ToString()));
        }

        return [.. enrichers];
    }

    /// <summary>
    /// Unwinds in the reverse of the order things were pushed. The activity is stopped before the
    /// ambient context is restored so its duration covers the handlers and nothing after them.
    /// </summary>
    private sealed class DispatchScope(
        IDisposable logScope,
        Activity? activity,
        IDisposable ambient,
        Activity? previousActivity) : IDisposable
    {
        public void Dispose()
        {
            logScope.Dispose();

            // Stopping a root activity leaves Activity.Current null (its parent), so the job's own
            // ambient activity has to be put back by hand or the next message would start from
            // nothing.
            activity?.Dispose();

            Activity.Current = previousActivity;

            ambient.Dispose();
        }
    }
}

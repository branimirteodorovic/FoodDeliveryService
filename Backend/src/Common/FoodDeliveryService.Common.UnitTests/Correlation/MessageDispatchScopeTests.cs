using System.Diagnostics;
using AwesomeAssertions;
using FoodDeliveryService.Common.Infrastructure.Correlation;
using FoodDeliveryService.Common.Presentation.Correlation;
using Serilog;
using Serilog.Core;
using Serilog.Events;

namespace FoodDeliveryService.Common.UnitTests.Correlation;

/// <summary>
/// The restore side of correlation across the outbox/inbox boundary. A row has been sitting in a
/// table since the request that produced it finished, and this is what makes the dispatch of that
/// row look like part of the same story: the id back in the log scope, the message's business ids
/// alongside it, and a span that points at the trace which caused the message.
/// <para>
/// The case that matters most operationally is the unhappy one — a row written before the columns
/// existed, or a traceparent that does not parse. Nothing here may throw, because it runs inside a
/// job loop where an exception would take the whole batch down.
/// </para>
/// </summary>
public class MessageDispatchScopeTests
{
    private const string TraceParent = "00-0af7651916cd43dd8448eb211c80319c-b7ad6b7169203331-01";
    private const string OriginTraceId = "0af7651916cd43dd8448eb211c80319c";
    private const string OriginSpanId = "b7ad6b7169203331";

    /// <summary>Stands in for whatever the job loop leaves current — in production, an Npgsql span.</summary>
    private const string AmbientSourceName = "FoodDeliveryService.Tests.MessageDispatch";

    [Fact]
    public void Begin_Should_RestoreTheStoredCorrelationIdIntoTheLogScope()
    {
        // Arrange
        var correlationContext = new CorrelationContext();

        using ActivityListener listener = ListenToDispatchSpans();

        // Act
        LogEvent logEvent = Capture(
            () => MessageDispatchScope.Begin(
                correlationContext,
                MessagingDiagnostics.OutboxDispatch,
                "OrderPlacedDomainEvent",
                "gateway-1234",
                TraceParent));

        // Assert — this is the whole point: a line written by a Quartz job, minutes after the
        // request returned, answers the same Seq query the request's own lines answer.
        Property(logEvent, "CorrelationId").Should().Be("gateway-1234");
        Property(logEvent, "TraceId").Should().NotBeNull();
        Property(logEvent, "SpanId").Should().NotBeNull();
    }

    [Fact]
    public void Begin_Should_LinkTheDispatchToTheStoredTraceContext()
    {
        // Arrange
        var correlationContext = new CorrelationContext();

        using ActivityListener listener = ListenToDispatchSpans();

        // Act
        using IDisposable scope = MessageDispatchScope.Begin(
            correlationContext,
            MessagingDiagnostics.OutboxDispatch,
            "OrderPlacedDomainEvent",
            "gateway-1234",
            TraceParent);

        Activity? dispatch = Activity.Current;

        // Assert
        dispatch.Should().NotBeNull();

        ActivityLink link = dispatch.Links.Should().ContainSingle().Subject;

        link.Context.TraceId.ToString().Should().Be(OriginTraceId);
        link.Context.SpanId.ToString().Should().Be(OriginSpanId);
    }

    [Fact]
    public void Begin_Should_StartANewTrace_RatherThanContinueTheStoredOne()
    {
        // Arrange — the deliberate trade. One job execution drains a batch belonging to N different
        // requests, so it cannot honestly be a child of any one of them, and a true continuation
        // would let a retrying message append to a trace for an hour. Change the link to a parent
        // and this assertion is what tells you the trade was reversed.
        //
        // The ambient activity is the other half of it, and the reason this failed on the real host
        // first: ActivitySource reads a default parentContext as "no parent GIVEN" and falls back to
        // Activity.Current, which inside the job loop is the Npgsql span of the batch SELECT. A
        // dispatch silently parented to a database read is neither a root nor a continuation.
        using var ambientSource = new ActivitySource(AmbientSourceName);
        using ActivityListener ambientListener = ListenTo(AmbientSourceName);
        using ActivityListener listener = ListenToDispatchSpans();

        using Activity? ambient = ambientSource.StartActivity("the job loop");

        ambient.Should().NotBeNull();

        var correlationContext = new CorrelationContext();

        // Act
        using IDisposable scope = MessageDispatchScope.Begin(
            correlationContext,
            MessagingDiagnostics.OutboxDispatch,
            "OrderPlacedDomainEvent",
            "gateway-1234",
            TraceParent);

        // Assert
        Activity dispatch = Activity.Current.Should().NotBeNull().And.Subject.As<Activity>();

        dispatch.TraceId.ToString().Should().NotBe(OriginTraceId);
        dispatch.TraceId.Should().NotBe(ambient.TraceId);
        dispatch.ParentSpanId.Should().Be(default(ActivitySpanId));
    }

    [Fact]
    public void Dispose_Should_PutBackTheAmbientActivity()
    {
        // Arrange — stopping a root activity leaves Activity.Current at its parent, which is null.
        // Without an explicit restore the job loop would lose its own ambient span after the first
        // message it dispatched.
        using var ambientSource = new ActivitySource(AmbientSourceName);
        using ActivityListener ambientListener = ListenTo(AmbientSourceName);
        using ActivityListener listener = ListenToDispatchSpans();

        using Activity? ambient = ambientSource.StartActivity("the job loop");

        var correlationContext = new CorrelationContext();

        // Act
        MessageDispatchScope
            .Begin(correlationContext, MessagingDiagnostics.OutboxDispatch, "OrderPlacedDomainEvent", "first", null)
            .Dispose();

        // Assert
        Activity.Current.Should().Be(ambient);
    }

    [Theory]
    // The pre-migration row: the columns exist but this row predates them.
    [InlineData(null)]
    [InlineData("")]
    // Not a W3C traceparent at all.
    [InlineData("nonsense")]
    // Truncated at the column width — the shape a too-long value would arrive in.
    [InlineData("00-0af7651916cd43dd8448eb211c80319c-b7ad6b716")]
    public void Begin_Should_DegradeToNoLink_WhenTheTraceParentIsUnusable(string? traceParent)
    {
        // Arrange
        var correlationContext = new CorrelationContext();

        using ActivityListener listener = ListenToDispatchSpans();

        // Act — must not throw: this runs inside a job loop, where it would take the batch with it.
        using IDisposable scope = MessageDispatchScope.Begin(
            correlationContext,
            MessagingDiagnostics.InboxDispatch,
            "UserRegisteredIntegrationEvent",
            "gateway-1234",
            traceParent);

        // Assert — no link, and the id is still restored, so the row dispatches exactly as it always
        // did and merely loses the trace navigation.
        Activity.Current.Should().NotBeNull();
        Activity.Current!.Links.Should().BeEmpty();
        correlationContext.CorrelationId.Should().Be("gateway-1234");
    }

    [Fact]
    public void Begin_Should_FallBackToTheDispatchTrace_WhenTheRowCarriesNoCorrelationId()
    {
        // Arrange — a pre-migration row, or work that originated in a job.
        Activity.Current = null;

        var correlationContext = new CorrelationContext();

        using ActivityListener listener = ListenToDispatchSpans();

        // Act
        LogEvent logEvent = Capture(
            () => MessageDispatchScope.Begin(
                correlationContext,
                MessagingDiagnostics.OutboxDispatch,
                "OrderPlacedDomainEvent",
                correlationId: null,
                traceParent: null));

        // Assert — an id, not an absent property: the flow is still searchable, it just has no
        // request behind it to inherit one from.
        Property(logEvent, "CorrelationId").Should().Be(Property(logEvent, "TraceId"));
    }

    [Fact]
    public void Begin_Should_MakeTheStoredContextAmbient()
    {
        // Arrange — the chain: a handler running under this scope publishes (the publish filter
        // reads the id off the context) and may save (the outbox interceptor stamps the next row
        // from the same context). Both must see the ORIGINATING context, not this dispatch's.
        var correlationContext = new CorrelationContext();

        using ActivityListener listener = ListenToDispatchSpans();

        // Act
        using IDisposable scope = MessageDispatchScope.Begin(
            correlationContext,
            MessagingDiagnostics.OutboxDispatch,
            "OrderPlacedDomainEvent",
            "gateway-1234",
            TraceParent);

        // Assert
        correlationContext.CorrelationId.Should().Be("gateway-1234");
        correlationContext.TraceParent.Should().Be(TraceParent);
    }

    [Fact]
    public void Dispose_Should_UnwindTheScope()
    {
        // Arrange — the loop pushes once per message; message two must not inherit message one.
        Activity.Current = null;

        var correlationContext = new CorrelationContext();

        using ActivityListener listener = ListenToDispatchSpans();

        // Act
        MessageDispatchScope
            .Begin(correlationContext, MessagingDiagnostics.OutboxDispatch, "OrderPlacedDomainEvent", "first", null)
            .Dispose();

        // Assert
        correlationContext.CorrelationId.Should().BeNull();
        Activity.Current.Should().BeNull();
    }

    [Fact]
    public void PushBusinessIds_Should_PushTheAggregateIdsTheMessageCarries()
    {
        // Arrange
        var message = new TestEvent(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "irrelevant");

        // Act
        LogEvent logEvent = Capture(() => MessageDispatchScope.PushBusinessIds(message));

        // Assert — what makes OrderId = '…' in Seq return the placement request AND the outbox
        // dispatch AND the consuming handler, instead of only the first.
        Property(logEvent, "OrderId").Should().Be(message.OrderId.ToString());
        Property(logEvent, "DriverId").Should().Be(message.DriverId.ToString());
    }

    [Fact]
    public void PushBusinessIds_Should_IgnoreTheEventsOwnIdAndItsNonIdProperties()
    {
        // Arrange
        var message = new TestEvent(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "irrelevant");

        // Act
        LogEvent logEvent = Capture(() => MessageDispatchScope.PushBusinessIds(message));

        // Assert — the event's own Id names the message, not an aggregate, and every module has one:
        // logging it under a name that generic would collide across the whole platform.
        logEvent.Properties.Should().NotContainKeys("Id", "Name");
    }

    /// <summary>
    /// Opens the scope, writes one line through a real Serilog pipeline with
    /// <c>Enrich.FromLogContext()</c>, and returns it — the properties only exist if the scope was
    /// actually pushed.
    /// </summary>
    private static LogEvent Capture(Func<IDisposable> openScope)
    {
        var sink = new CapturingSink();

        using Logger logger = new LoggerConfiguration()
            .MinimumLevel.Verbose()
            .Enrich.FromLogContext()
            .WriteTo.Sink(sink)
            .CreateLogger();

        using (openScope())
        {
            logger.Information("inside the dispatch");
        }

        return sink.Events.Should().ContainSingle().Subject;
    }

    private static ActivityListener ListenToDispatchSpans() => ListenTo(MessagingDiagnostics.Name);

    private static ActivityListener ListenTo(string sourceName)
    {
        var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == sourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData
        };

        ActivitySource.AddActivityListener(listener);

        return listener;
    }

    private static string? Property(LogEvent logEvent, string name) =>
        logEvent.Properties.TryGetValue(name, out LogEventPropertyValue? value) && value is ScalarValue scalar
            ? scalar.Value?.ToString()
            : null;

    private sealed record TestEvent(Guid Id, Guid OrderId, Guid DriverId, string Name);

    private sealed class CapturingSink : ILogEventSink
    {
        public List<LogEvent> Events { get; } = [];

        public void Emit(LogEvent logEvent) => Events.Add(logEvent);
    }
}

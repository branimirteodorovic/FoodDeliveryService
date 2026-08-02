using System.Diagnostics;
using AwesomeAssertions;
using FoodDeliveryService.Common.Presentation.Correlation;
using FoodDeliveryService.Common.Presentation.Telemetry;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Patterns;
using Serilog;
using Serilog.Core;
using Serilog.Events;

namespace FoodDeliveryService.Common.UnitTests.Correlation;

/// <summary>
/// What the middleware promises is that a log line written anywhere inside the request carries the
/// ids needed to pivot from it: to the trace (TraceId), to the operation inside the trace (SpanId),
/// to the service (ServiceName), to the customer's complaint (CorrelationId) and to everything else
/// about the same order (the business id on the route). Each assertion here logs through a real
/// Serilog pipeline with <c>Enrich.FromLogContext()</c>, because the properties only exist if the
/// scope is actually pushed — and only survive if it is still open when the line is written.
/// </summary>
public class LogContextTraceLoggingMiddlewareTests
{
    private const string ActivitySourceName = "FoodDeliveryService.Tests.LogContext";
    private const string ServiceName = "FoodDeliveryService.Orders.Api";

    [Fact]
    public async Task Invoke_Should_PushTheTraceAndSpanOfTheAmbientActivity()
    {
        // Arrange
        using var source = new ActivitySource(ActivitySourceName);
        using ActivityListener listener = CreateListener();

        ActivitySource.AddActivityListener(listener);

        using Activity? activity = source.StartActivity("request");

        activity.Should().NotBeNull();

        // Act
        LogEvent logEvent = await CaptureAsync(new DefaultHttpContext());

        // Assert — the SpanId is the half that was missing before this milestone: TraceId alone
        // finds the trace, SpanId finds the operation inside it that wrote the line.
        Property(logEvent, "TraceId").Should().Be(activity.TraceId.ToString());
        Property(logEvent, "SpanId").Should().Be(activity.SpanId.ToString());
    }

    [Fact]
    public async Task Invoke_Should_PushTheServiceName()
    {
        // Act
        LogEvent logEvent = await CaptureAsync(new DefaultHttpContext());

        // Assert — the same value the host reports to Jaeger as service.name, so a log filter and a
        // trace filter take the same string.
        Property(logEvent, "ServiceName").Should().Be(ServiceName);
    }

    [Fact]
    public async Task Invoke_Should_PushTheCorrelationIdResolvedUpstream()
    {
        // Arrange — CorrelationIdMiddleware runs first and parks its answer here.
        const string correlationId = "0af7651916cd43dd8448eb211c80319c";

        var context = new DefaultHttpContext();

        context.Items[CorrelationHeaders.CorrelationIdItemKey] = correlationId;

        // Act
        LogEvent logEvent = await CaptureAsync(context);

        // Assert
        Property(logEvent, "CorrelationId").Should().Be(correlationId);
    }

    [Fact]
    public async Task Invoke_Should_OmitTheTraceProperties_WhenNothingIsTracing()
    {
        // Arrange
        Activity.Current = null;

        // Act
        LogEvent logEvent = await CaptureAsync(new DefaultHttpContext());

        // Assert — no trace to point at means no property, rather than a property whose value is
        // the string "null" that every Seq query then has to exclude.
        logEvent.Properties.Should().NotContainKeys("TraceId", "SpanId");
    }

    [Theory]
    // The bare `id` every endpoint uses for its own aggregate is qualified by the resource it
    // follows, so seven modules don't all log an unsearchable "Id".
    [InlineData("orders/{id:guid}/cancel", "id", "OrderId")]
    [InlineData("delivery/drivers/{id:guid}", "id", "DriverId")]
    [InlineData("delivery/deliveries/{id:guid}/accept", "id", "DeliveryId")]
    // A parameter that names itself is taken as it stands.
    [InlineData("restaurants/{restaurantId:guid}/menu", "restaurantId", "RestaurantId")]
    public async Task Invoke_Should_PushTheBusinessIdOnTheRoute(string pattern, string parameter, string property)
    {
        // Arrange
        var businessId = Guid.NewGuid();

        HttpContext context = CreateRoutedContext(pattern, parameter, businessId);

        // Act
        LogEvent logEvent = await CaptureAsync(context);

        // Assert — this is what makes Seq searchable by order: OrderId = '…' returns every line the
        // platform wrote about that order.
        Property(logEvent, property).Should().Be(businessId.ToString());
    }

    [Fact]
    public async Task Invoke_Should_IgnoreRouteValuesThatAreNotIds()
    {
        // Arrange
        HttpContext context = CreateRoutedContext("restaurants/{slug}", "slug", "pizza-place");

        // Act
        LogEvent logEvent = await CaptureAsync(context);

        // Assert — route values are caller-controlled; only the id parameters are promoted, and the
        // rest stay in the request log where they belong.
        logEvent.Properties.Should().NotContainKey("Slug");
    }

    [Fact]
    public async Task Invoke_Should_KeepTheScopeOpenAcrossAnAwait()
    {
        // Arrange — the regression test for the bug in all seven copies this replaces: they returned
        // the inner task from inside a `using`, disposing the scope as soon as the first await
        // yielded, so the properties covered only the synchronous head of the pipeline.
        var sink = new CapturingSink();

        using Logger logger = BuildLogger(sink);

        var middleware = new LogContextTraceLoggingMiddleware(
            async _ =>
            {
                await Task.Yield();

                logger.Information("after the yield");
            },
            new HostServiceName(ServiceName));

        // Act
        await middleware.Invoke(new DefaultHttpContext());

        // Assert
        Property(sink.Events.Should().ContainSingle().Subject, "ServiceName").Should().Be(ServiceName);
    }

    private static async Task<LogEvent> CaptureAsync(HttpContext context)
    {
        var sink = new CapturingSink();

        using Logger logger = BuildLogger(sink);

        var middleware = new LogContextTraceLoggingMiddleware(
            _ =>
            {
                logger.Information("inside the request");

                return Task.CompletedTask;
            },
            new HostServiceName(ServiceName));

        await middleware.Invoke(context);

        return sink.Events.Should().ContainSingle().Subject;
    }

    private static Logger BuildLogger(CapturingSink sink) =>
        new LoggerConfiguration()
            .MinimumLevel.Verbose()
            .Enrich.FromLogContext()
            .WriteTo.Sink(sink)
            .CreateLogger();

    private static DefaultHttpContext CreateRoutedContext(string pattern, string parameter, object value)
    {
        var context = new DefaultHttpContext();

        // The endpoint carries the route PATTERN, which is where the resource name preceding a bare
        // `id` is read from; the route values carry what the request actually matched.
        context.SetEndpoint(new RouteEndpoint(
            _ => Task.CompletedTask,
            RoutePatternFactory.Parse(pattern),
            order: 0,
            new EndpointMetadataCollection(),
            displayName: pattern));

        context.Request.RouteValues[parameter] = value;

        return context;
    }

    private static string? Property(LogEvent logEvent, string name) =>
        logEvent.Properties.TryGetValue(name, out LogEventPropertyValue? value) && value is ScalarValue scalar
            ? scalar.Value?.ToString()
            : null;

    private static ActivityListener CreateListener() =>
        new()
        {
            ShouldListenTo = source => source.Name == ActivitySourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData
        };

    private sealed class CapturingSink : ILogEventSink
    {
        public List<LogEvent> Events { get; } = [];

        public void Emit(LogEvent logEvent) => Events.Add(logEvent);
    }
}

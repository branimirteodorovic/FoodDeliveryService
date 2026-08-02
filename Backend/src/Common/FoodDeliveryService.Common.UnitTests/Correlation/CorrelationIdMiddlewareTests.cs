using System.Diagnostics;
using AwesomeAssertions;
using FoodDeliveryService.Common.Presentation.Correlation;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;

namespace FoodDeliveryService.Common.UnitTests.Correlation;

/// <summary>
/// The correlation id is the one string a support agent copies out of a failed response, so the two
/// things that must never break are that an inbound id survives every hop (otherwise each service
/// invents its own and the id ties nothing together) and that a missing one defaults to the trace
/// id (otherwise it ties the logs to no trace).
/// </summary>
public class CorrelationIdMiddlewareTests
{
    private const string ActivitySourceName = "FoodDeliveryService.Tests.Correlation";

    [Fact]
    public async Task Invoke_Should_PreserveAWellFormedInboundId()
    {
        // Arrange — what the Gateway forwards to a module host.
        const string inbound = "0af7651916cd43dd8448eb211c80319c";

        HttpContext context = CreateContext(inbound);

        // Act
        await InvokeAsync(context);

        // Assert — the same id downstream, in the log scope, and back to the caller.
        RequestId(context).Should().Be(inbound);
        context.Items[CorrelationHeaders.CorrelationIdItemKey].Should().Be(inbound);
        (await StartResponseAsync(context)).Should().Be(inbound);
    }

    [Theory]
    // A header sent twice arrives comma-joined — two ids are no id.
    [InlineData("first-id,second-id")]
    // Response-splitting shapes, and anything else outside the accepted alphabet.
    [InlineData("id\r\nX-Injected: value")]
    [InlineData("id with spaces")]
    [InlineData("")]
    public async Task Invoke_Should_ReplaceAMalformedInboundId(string inbound)
    {
        // Arrange
        HttpContext context = CreateContext(inbound);

        // Act
        await InvokeAsync(context);

        // Assert — a malformed correlation header never fails a customer's request. The middleware
        // replaces it, so nothing outside the accepted alphabet reaches the client or a log
        // property.
        string resolved = RequestId(context);

        resolved.Should().NotBe(inbound).And.MatchRegex("^[A-Za-z0-9]+$");
    }

    [Fact]
    public async Task Invoke_Should_ReplaceAnOversizedInboundId()
    {
        // Arrange — the cap exists so a hostile client cannot put kilobytes on every log line.
        string inbound = new('a', 129);

        HttpContext context = CreateContext(inbound);

        // Act
        await InvokeAsync(context);

        // Assert
        RequestId(context).Should().NotBe(inbound);
    }

    [Fact]
    public async Task Invoke_Should_DefaultToTheTraceId_WhenNoIdIsInbound()
    {
        // Arrange — the whole point of the default: the id the client is handed IS the trace id, so
        // one string finds the Seq logs and the Jaeger trace without joining two id spaces.
        using var source = new ActivitySource(ActivitySourceName);
        using ActivityListener listener = CreateListener();

        ActivitySource.AddActivityListener(listener);

        using Activity? activity = source.StartActivity("request");

        activity.Should().NotBeNull();

        HttpContext context = CreateContext(inbound: null);

        // Act
        await InvokeAsync(context);

        // Assert
        RequestId(context).Should().Be(activity.TraceId.ToString());
    }

    [Fact]
    public async Task Invoke_Should_GenerateAnId_WhenThereIsNoTraceToBorrowFrom()
    {
        // Arrange — a host with no tracing configured. The response still has to carry something.
        Activity.Current = null;

        HttpContext context = CreateContext(inbound: null);

        // Act
        await InvokeAsync(context);

        // Assert
        string resolved = RequestId(context);

        resolved.Should().NotBeEmpty();
        (await StartResponseAsync(context)).Should().Be(resolved);
    }

    private static Task InvokeAsync(HttpContext context) =>
        new CorrelationIdMiddleware(_ => Task.CompletedTask).Invoke(context);

    /// <summary>
    /// The id as the downstream service sees it: written back onto the REQUEST headers, which is
    /// what YARP copies to the proxied call.
    /// </summary>
    private static string RequestId(HttpContext context) =>
        context.Request.Headers[CorrelationHeaders.CorrelationId].ToString();

    /// <summary>
    /// The response header is written from an <c>OnStarting</c> callback — so an exception handler
    /// that resets the response cannot drop it from exactly the 500s whose id someone wants — and a
    /// <see cref="DefaultHttpContext"/> never starts a response by itself. Firing the callbacks is
    /// what a real server does when the first byte goes out.
    /// </summary>
    private static async Task<string> StartResponseAsync(HttpContext context)
    {
        var feature = (RecordingResponseFeature)context.Features.Get<IHttpResponseFeature>()!;

        await feature.FireOnStartingAsync();

        return context.Response.Headers[CorrelationHeaders.CorrelationId].ToString();
    }

    private static DefaultHttpContext CreateContext(string? inbound)
    {
        var context = new DefaultHttpContext();

        context.Features.Set<IHttpResponseFeature>(new RecordingResponseFeature());

        if (inbound is not null)
        {
            context.Request.Headers[CorrelationHeaders.CorrelationId] = inbound;
        }

        return context;
    }

    private static ActivityListener CreateListener() =>
        new()
        {
            ShouldListenTo = source => source.Name == ActivitySourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData
        };

    /// <summary>
    /// <see cref="DefaultHttpContext"/>'s own response feature drops <c>OnStarting</c> callbacks on
    /// the floor, which would make the echoed header untestable outside a running server.
    /// </summary>
    private sealed class RecordingResponseFeature : IHttpResponseFeature
    {
        private readonly List<(Func<object, Task> Callback, object State)> _onStarting = [];

        public IHeaderDictionary Headers { get; set; } = new HeaderDictionary();

        public Stream Body { get; set; } = Stream.Null;

        public int StatusCode { get; set; } = StatusCodes.Status200OK;

        public string? ReasonPhrase { get; set; }

        public bool HasStarted { get; private set; }

        public void OnStarting(Func<object, Task> callback, object state) => _onStarting.Add((callback, state));

        public void OnCompleted(Func<object, Task> callback, object state)
        {
            // Nothing under test registers one.
        }

        public async Task FireOnStartingAsync()
        {
            HasStarted = true;

            foreach ((Func<object, Task> callback, object state) in _onStarting)
            {
                await callback(state);
            }
        }
    }
}

using System.Diagnostics;
using AwesomeAssertions;
using FoodDeliveryService.Common.Domain;
using FoodDeliveryService.Common.Presentation.Correlation;
using FoodDeliveryService.Modules.Orders.IntegrationTests.Abstractions;
using MassTransit.Logging;
using Serilog.Events;

namespace FoodDeliveryService.Modules.Orders.IntegrationTests.Telemetry;

/// <summary>
/// Correlation on the real host: the header a client sees, the properties a Seq line carries, and
/// the one that cannot be tested anywhere but here — that a trace survives the trip across RabbitMQ
/// from one service's outbox to another's consumer.
/// </summary>
public class CorrelationTests(IntegrationTestWebAppFactory factory) : BaseIntegrationTest(factory)
{
    [Fact]
    public async Task AResponse_Should_CarryACorrelationId()
    {
        // Arrange
        HttpClient client = await GetAuthenticatedHttpClientAsync();

        // Act
        HttpResponseMessage response = await client.GetAsync("orders", TestContext.Current.CancellationToken);

        response.EnsureSuccessStatusCode();

        // Assert — a client that sent no id gets the request's trace id back, which is the id a
        // support agent then searches Seq and Jaeger with.
        string correlationId = CorrelationIdOf(response);

        correlationId.Should().MatchRegex("^[0-9a-f]{32}$");
    }

    [Fact]
    public async Task AnInboundCorrelationId_Should_BePreserved()
    {
        // Arrange — what the Gateway forwards after stamping it.
        string inbound = $"gateway-{Guid.NewGuid():N}";

        HttpClient client = await GetAuthenticatedHttpClientAsync();

        using var request = new HttpRequestMessage(HttpMethod.Get, "orders");
        request.Headers.Add(CorrelationHeaders.CorrelationId, inbound);

        // Act
        HttpResponseMessage response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        response.EnsureSuccessStatusCode();

        // Assert — the id is per request, not per hop: a service that receives one never mints its
        // own, or the id would tie nothing together.
        CorrelationIdOf(response).Should().Be(inbound);
    }

    [Fact]
    public async Task LogsOfARequest_Should_CarryTheTraceSpanAndService()
    {
        // Arrange — a unique inbound id is how this test finds its own log lines among everything
        // else the host writes (outbox jobs, other tests in the collection).
        string inbound = $"logs-{Guid.NewGuid():N}";

        HttpClient client = await GetAuthenticatedHttpClientAsync();

        using var request = new HttpRequestMessage(HttpMethod.Get, "orders");
        request.Headers.Add(CorrelationHeaders.CorrelationId, inbound);

        // Act
        HttpResponseMessage response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        response.EnsureSuccessStatusCode();

        // Assert — RequestLoggingPipelineBehavior logs inside the MediatR pipeline, well past the
        // first await of the request, so these properties are only here if the log scope is still
        // open there.
        IReadOnlyList<LogEvent> logEvents = [.. LogEventsFor(inbound)];

        logEvents.Should().NotBeEmpty("the request's log scope must carry the correlation id");

        LogEvent logEvent = logEvents[0];

        Property(logEvent, "TraceId").Should().MatchRegex("^[0-9a-f]{32}$");
        Property(logEvent, "SpanId").Should().MatchRegex("^[0-9a-f]{16}$");
        Property(logEvent, "ServiceName").Should().Be("FoodDeliveryService.Orders.Api");
    }

    [Fact]
    public async Task LogsOfARequest_Should_CarryTheBusinessIdOnTheRoute()
    {
        // Arrange
        string inbound = $"order-{Guid.NewGuid():N}";

        HttpClient client = await GetAuthenticatedHttpClientAsync();

        PlacedOrder placedOrder = await PlaceOrderAsync(client);

        using var request = new HttpRequestMessage(HttpMethod.Get, $"orders/{placedOrder.OrderId}");
        request.Headers.Add(CorrelationHeaders.CorrelationId, inbound);

        // Act
        HttpResponseMessage response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        response.EnsureSuccessStatusCode();

        // Assert — this is what makes Seq searchable by order rather than by request id only. The
        // route parameter is the bare `id`, qualified by the resource it follows.
        LogEventsFor(inbound)
            .Should().Contain(logEvent => Property(logEvent, "OrderId") == placedOrder.OrderId.ToString());
    }

    [Fact]
    public async Task AnEventCrossingTheBus_Should_StayInOneTrace()
    {
        // Act — registering a customer against the in-process Users host publishes
        // UserRegisteredIntegrationEvent through its outbox; the Orders host consumes it to build
        // its customer replica. Producer and consumer are different processes' worth of DI, joined
        // only by the traceparent MassTransit puts on the message.
        await RegisterCustomerAndGetTokenAsync();

        // Assert
        Result<LinkedSpans> linked = await Poller.WaitAsync(TimeSpan.FromSeconds(60), FindLinkedSpansAsync);

        linked.IsSuccess.Should().BeTrue(
            "a MassTransit span in the Orders host must be the child of a span published by the Users host");

        // The guard this exists for: publishing outside IEventBus, or losing the OTel MassTransit
        // source, breaks the link and leaves two disconnected traces where one distributed one was.
        linked.Value.Consumer.TraceId.Should().Be(linked.Value.Producer.TraceId);
        linked.Value.Consumer.ParentSpanId.Should().Be(linked.Value.Producer.SpanId);
    }

    /// <summary>
    /// Looks for a MassTransit span in the Orders host whose parent span was recorded in the Users
    /// host. Matching on the parent/child ids rather than on message metadata keeps the assertion
    /// independent of MassTransit's tag names — and a cross-process parent can only exist if the
    /// trace context travelled with the message.
    /// </summary>
    private Task<Result<LinkedSpans>> FindLinkedSpansAsync()
    {
        IReadOnlyList<Activity> ordersSpans = Factory.CollectActivities();
        IReadOnlyList<Activity> usersSpans = Factory.UsersApi.CollectActivities();

        foreach (Activity consumer in ordersSpans)
        {
            if (consumer.Source.Name != DiagnosticHeaders.DefaultListenerName ||
                consumer.ParentSpanId == default)
            {
                continue;
            }

            Activity? producer = usersSpans.FirstOrDefault(span => span.SpanId == consumer.ParentSpanId);

            if (producer is not null)
            {
                return Task.FromResult(Result.Success(new LinkedSpans(producer, consumer)));
            }
        }

        return Task.FromResult(Result.Failure<LinkedSpans>(Error.NullValue));
    }

    private static string CorrelationIdOf(HttpResponseMessage response) =>
        response.Headers.TryGetValues(CorrelationHeaders.CorrelationId, out IEnumerable<string>? values)
            ? values.Single()
            : string.Empty;

    private IEnumerable<LogEvent> LogEventsFor(string correlationId) =>
        Factory.CollectLogEvents().Where(logEvent => Property(logEvent, "CorrelationId") == correlationId);

    private static string? Property(LogEvent logEvent, string name) =>
        logEvent.Properties.TryGetValue(name, out LogEventPropertyValue? value) && value is ScalarValue scalar
            ? scalar.Value?.ToString()
            : null;

    private sealed record LinkedSpans(Activity Producer, Activity Consumer);
}

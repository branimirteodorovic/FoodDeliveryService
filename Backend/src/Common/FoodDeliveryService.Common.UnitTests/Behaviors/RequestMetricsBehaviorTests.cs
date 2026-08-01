using System.Diagnostics.Metrics;
using AwesomeAssertions;
using FoodDeliveryService.Common.Application.Behaviors;
using FoodDeliveryService.Common.Application.Caching;
using FoodDeliveryService.Common.Application.Diagnostics;
using FoodDeliveryService.Common.Domain;
using FoodDeliveryService.Common.Infrastructure.Caching;
using MediatR;
using Microsoft.Extensions.DependencyInjection;

namespace FoodDeliveryService.Common.UnitTests.Behaviors;

/// <summary>
/// The RED signal every command and query emits. <c>ApplicationDiagnostics</c> is a process-wide
/// static meter and xUnit runs test classes in parallel, so each test here drives its own request
/// type and the recorder filters measurements by the <c>request</c> tag — the same isolation
/// technique <c>CacheDiagnosticsTests</c> uses with its key prefix.
/// <para>
/// These prove emission. That the meter is actually <i>collected</i> by a host's provider is proved
/// in <c>Orders.IntegrationTests</c>, against the real <c>MeterProvider</c>.
/// </para>
/// </summary>
public class RequestMetricsBehaviorTests
{
    private const string RequestsInstrument = "app.requests";
    private const string DurationInstrument = "app.request.duration";
    private const string FailuresInstrument = "app.request.failures";

    private readonly ICacheService _cacheService = new ServiceCollection()
        .AddLogging()
        .AddDistributedMemoryCache()
        .AddSingleton<ICacheService, CacheService>()
        .BuildServiceProvider()
        .GetRequiredService<ICacheService>();

    [Fact]
    public async Task Handle_Should_CountAndTimeTheRequest_OnSuccess()
    {
        // Arrange
        var behavior = new RequestMetricsBehavior<SucceedingQuery, Result<string>>();

        using var recorder = new RequestMeasurementRecorder(nameof(SucceedingQuery));

        // Act
        Result<string> result = await behavior.Handle(
            new SucceedingQuery(),
            _ => Task.FromResult(Result.Success("value")),
            TestContext.Current.CancellationToken);

        // Assert
        result.IsSuccess.Should().BeTrue();

        recorder.Total(RequestsInstrument).Should().Be(1);
        recorder.Count(DurationInstrument).Should().Be(1);
        recorder.Total(FailuresInstrument).Should().Be(0);
        recorder.Outcomes(RequestsInstrument).Should().AllBe("success");
    }

    [Fact]
    public async Task Handle_Should_CountTheFailure_WithTheErrorType_OnAFailureResult()
    {
        // Arrange
        var behavior = new RequestMetricsBehavior<FailingQuery, Result<string>>();
        var error = Error.NotFound("Test.NotFound", "not found");

        using var recorder = new RequestMeasurementRecorder(nameof(FailingQuery));

        // Act
        Result<string> result = await behavior.Handle(
            new FailingQuery(),
            _ => Task.FromResult(Result.Failure<string>(error)),
            TestContext.Current.CancellationToken);

        // Assert — a business failure is still a request, so it lands in both instruments: the rate
        // and the error ratio a RED panel draws share one denominator.
        result.IsFailure.Should().BeTrue();

        recorder.Total(RequestsInstrument).Should().Be(1);
        recorder.Total(FailuresInstrument).Should().Be(1);
        recorder.Outcomes(RequestsInstrument).Should().AllBe("failure");
        recorder.ErrorTypes(FailuresInstrument).Should().AllBe(nameof(ErrorType.NotFound));
    }

    [Fact]
    public async Task Handle_Should_CountAndRethrow_WhenTheHandlerThrows()
    {
        // Arrange
        var behavior = new RequestMetricsBehavior<ThrowingCommand, Result>();

        using var recorder = new RequestMeasurementRecorder(nameof(ThrowingCommand));

        // Act
        Func<Task> act = () => behavior.Handle(
            new ThrowingCommand(),
            _ => throw new InvalidOperationException("boom"),
            TestContext.Current.CancellationToken);

        // Assert — ExceptionHandlingPipelineBehavior sits outside this one and rethrows rather than
        // converting to a failure Result, so if this behavior swallowed or skipped the throw, the
        // errors most worth alerting on would be missing from every RED panel.
        await act.Should().ThrowAsync<InvalidOperationException>();

        recorder.Total(RequestsInstrument).Should().Be(1);
        recorder.Total(FailuresInstrument).Should().Be(1);
        recorder.Outcomes(RequestsInstrument).Should().AllBe("exception");
        recorder.ErrorTypes(FailuresInstrument).Should().AllBe("exception");
    }

    [Fact]
    public async Task Handle_Should_ReturnTheResponseUnchanged()
    {
        // Arrange
        var behavior = new RequestMetricsBehavior<PassthroughQuery, Result<string>>();
        var expected = Result.Success("untouched");

        // Act
        Result<string> actual = await behavior.Handle(
            new PassthroughQuery(),
            _ => Task.FromResult(expected),
            TestContext.Current.CancellationToken);

        // Assert — measuring must never be observable in the response.
        actual.Should().BeSameAs(expected);
    }

    [Fact]
    public async Task Handle_Should_StillCountAQuery_ThatTheCacheServes()
    {
        // Arrange — the regression test for the behavior's position in the pipeline. Metrics wraps
        // caching, mirroring AddApplication's registration order.
        var caching = new QueryCachingBehavior<CachedMetricsQuery, Result<string>>(_cacheService);
        var metrics = new RequestMetricsBehavior<CachedMetricsQuery, Result<string>>();

        var request = new CachedMetricsQuery(CacheKeys.Create($"tests-{Guid.NewGuid():N}", "metrics"));

        await _cacheService.SetAsync(
            request.CacheKey,
            Result.Success("cached"),
            TimeSpan.FromMinutes(1),
            TestContext.Current.CancellationToken);

        var handlerCallCount = 0;

        Task<Result<string>> Handler(CancellationToken cancellationToken)
        {
            handlerCallCount++;
            return Task.FromResult(Result.Success("fresh"));
        }

        using var recorder = new RequestMeasurementRecorder(nameof(CachedMetricsQuery));

        // Act
        Result<string> result = await metrics.Handle(
            request,
            cancellationToken => caching.Handle(request, Handler, cancellationToken),
            TestContext.Current.CancellationToken);

        // Assert — the cache short-circuited (the handler never ran) and the request was STILL
        // measured. Registering metrics after the caching behavior would drop this measurement, and
        // the latency dashboard would then describe cache misses only.
        result.Value.Should().Be("cached");
        handlerCallCount.Should().Be(0);

        recorder.Total(RequestsInstrument).Should().Be(1);
        recorder.Count(DurationInstrument).Should().Be(1);
        recorder.Outcomes(RequestsInstrument).Should().AllBe("success");
    }

    private sealed record SucceedingQuery : IRequest<Result<string>>;

    private sealed record FailingQuery : IRequest<Result<string>>;

    private sealed record ThrowingCommand : IRequest<Result>;

    private sealed record PassthroughQuery : IRequest<Result<string>>;

    private sealed record CachedMetricsQuery(string CacheKey) : ICachedQuery
    {
        public TimeSpan? Expiration => TimeSpan.FromMinutes(1);
    }

    /// <summary>
    /// Captures measurements on the application meter for one request type. A raw
    /// <see cref="MeterListener"/> is enough here because these tests are about what the behavior
    /// records, not about whether a provider is listening — the latter is an integration concern and
    /// is asserted there.
    /// </summary>
    private sealed class RequestMeasurementRecorder : IDisposable
    {
        private const string RequestTagName = "request";
        private const string OutcomeTagName = "outcome";
        private const string ErrorTypeTagName = "error.type";

        private readonly MeterListener _listener = new();
        private readonly List<Measurement> _measurements = [];
        private readonly Lock _gate = new();
        private readonly string _requestName;

        public RequestMeasurementRecorder(string requestName)
        {
            _requestName = requestName;

            _listener.InstrumentPublished = (instrument, listener) =>
            {
                if (instrument.Meter.Name == ApplicationDiagnostics.Name)
                {
                    listener.EnableMeasurementEvents(instrument);
                }
            };

            _listener.SetMeasurementEventCallback<long>(
                (instrument, measurement, tags, _) => Record(instrument, measurement, tags));

            _listener.SetMeasurementEventCallback<double>(
                (instrument, measurement, tags, _) => Record(instrument, measurement, tags));

            _listener.Start();
        }

        public long Total(string instrument) => Snapshot(instrument).Sum(m => (long)m.Value);

        public int Count(string instrument) => Snapshot(instrument).Count;

        public IReadOnlyList<string?> Outcomes(string instrument) =>
            [.. Snapshot(instrument).Select(m => m.Outcome)];

        public IReadOnlyList<string?> ErrorTypes(string instrument) =>
            [.. Snapshot(instrument).Select(m => m.ErrorType)];

        public void Dispose() => _listener.Dispose();

        private List<Measurement> Snapshot(string instrument)
        {
            lock (_gate)
            {
                return [.. _measurements.Where(m => m.Instrument == instrument)];
            }
        }

        private void Record(Instrument instrument, double value, ReadOnlySpan<KeyValuePair<string, object?>> tags)
        {
            string? request = null;
            string? outcome = null;
            string? errorType = null;

            foreach (KeyValuePair<string, object?> tag in tags)
            {
                switch (tag.Key)
                {
                    case RequestTagName:
                        request = tag.Value as string;
                        break;
                    case OutcomeTagName:
                        outcome = tag.Value as string;
                        break;
                    case ErrorTypeTagName:
                        errorType = tag.Value as string;
                        break;
                    default:
                        break;
                }
            }

            // Other test classes drive the same process-wide instruments in parallel; anything
            // outside this test's own request type is somebody else's measurement.
            if (request != _requestName)
            {
                return;
            }

            lock (_gate)
            {
                _measurements.Add(new Measurement(instrument.Name, value, outcome, errorType));
            }
        }

        private sealed record Measurement(string Instrument, double Value, string? Outcome, string? ErrorType);
    }
}

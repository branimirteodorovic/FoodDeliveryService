using System.Globalization;
using System.Net;
using AwesomeAssertions;
using FoodDeliveryService.Common.Presentation.RateLimiting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace FoodDeliveryService.Common.UnitTests.RateLimiting;

/// <summary>
/// The limiter driven end to end over real HTTP — the same middleware, the same partitioners, the
/// same <c>OnRejected</c> the Gateway runs, in front of stand-in endpoints on the Gateway's public
/// paths.
/// <para>
/// It is here rather than in a module's integration suite because the thing under test is the *edge*,
/// and no module host has one: the Gateway is the only host with a limiter, by design (Hard Rule 10
/// — all external traffic goes through it, so one limiter is sufficient and a second on every hop
/// would multiply the limit). Kestrel on a loopback port is what makes the assertions real: a
/// <c>429</c>, a <c>Retry-After</c> header and a shed request are properties of the response a client
/// receives, not of a lease object.
/// </para>
/// </summary>
public class EdgeRateLimitingTests
{
    private const string ReadPath = "/restaurants";
    private const string CriticalPath = "/orders/8a2e6d1c-0000-4000-8000-000000000001/ready";

    [Fact]
    public async Task PerClientWindow_Should_Reject_TheRequestPastTheLimit()
    {
        // Arrange — three reads per window, a concurrency limit high enough that it is not what
        // answers, and one client (every request comes from the same loopback address).
        await using Edge edge = await Edge.StartAsync(new EdgeRateLimitingOptions
        {
            ReadPermitLimit = 3,
            WindowSeconds = 30,
            GlobalConcurrencyLimit = 64,
        });

        // Act
        HttpResponseMessage first = await edge.GetAsync(ReadPath);
        HttpResponseMessage second = await edge.GetAsync(ReadPath);
        HttpResponseMessage third = await edge.GetAsync(ReadPath);
        HttpResponseMessage fourth = await edge.GetAsync(ReadPath);

        // Assert — N served, the N+1th shed.
        first.StatusCode.Should().Be(HttpStatusCode.OK);
        second.StatusCode.Should().Be(HttpStatusCode.OK);
        third.StatusCode.Should().Be(HttpStatusCode.OK);
        fourth.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
    }

    [Fact]
    public async Task Rejection_Should_TellTheClientHowLongToWait()
    {
        // Arrange
        await using Edge edge = await Edge.StartAsync(new EdgeRateLimitingOptions
        {
            ReadPermitLimit = 1,
            WindowSeconds = 30,
            GlobalConcurrencyLimit = 64,
        });

        await edge.GetAsync(ReadPath);

        // Act
        HttpResponseMessage rejected = await edge.GetAsync(ReadPath);

        // Assert — without Retry-After a client can only retry immediately, which is precisely the
        // behaviour that turns an overloaded platform into an unavailable one. The value is the
        // remaining window, so it is bounded by it rather than being a fixed guess.
        rejected.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        rejected.Headers.RetryAfter.Should().NotBeNull();
        rejected.Headers.RetryAfter!.Delta.Should().NotBeNull();
        rejected.Headers.RetryAfter.Delta!.Value.Should().BeGreaterThan(TimeSpan.Zero)
            .And.BeLessThanOrEqualTo(TimeSpan.FromSeconds(30));

        rejected.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");
    }

    [Theory]
    // The blackbox exporter probes every host every 15 s from outside the platform. A throttled
    // probe reports the outage it just caused.
    [InlineData("/health/live")]
    [InlineData("/health/ready")]
    // negotiate-then-connect is one logical SignalR connection over two requests.
    [InlineData("/hubs/tracking/negotiate")]
    public async Task ExemptPaths_Should_NeverBeLimited(string path)
    {
        // Arrange — a budget of one, which every other path would exhaust immediately.
        await using Edge edge = await Edge.StartAsync(new EdgeRateLimitingOptions
        {
            ReadPermitLimit = 1,
            WritePermitLimit = 1,
            CriticalPermitLimit = 1,
            WindowSeconds = 30,
            GlobalConcurrencyLimit = 64,
        });

        // Act
        HttpStatusCode[] statuses = await Task.WhenAll(
            Enumerable.Range(0, 10).Select(async _ => (await edge.GetAsync(path)).StatusCode));

        // Assert
        statuses.Should().AllBeEquivalentTo(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GlobalConcurrencyLimit_Should_ShedBrowsingButNotALifecycleTransition()
    {
        // Arrange — the shaped-shedding claim, made concrete. One concurrency slot, held by a read
        // that is parked inside its handler; per-client budgets high enough that the concurrency
        // limit is unambiguously what answers.
        await using Edge edge = await Edge.StartAsync(new EdgeRateLimitingOptions
        {
            GlobalConcurrencyLimit = 1,
            ReadPermitLimit = 100,
            CriticalPermitLimit = 100,
            WindowSeconds = 30,
        });

        edge.ParkRequests();

        Task<HttpResponseMessage> parked = edge.GetAsync(ReadPath);

        await edge.WaitForRequestInFlightAsync();

        // Act — a second browse, and a driver marking an order ready, while the only slot is taken.
        HttpResponseMessage shedBrowse = await edge.GetAsync(ReadPath);
        HttpResponseMessage lifecycle = await edge.PostAsync(CriticalPath);

        edge.ReleaseParkedRequests();

        // Assert — browsing degrades, the delivery lifecycle does not. That asymmetry is the whole
        // argument for tiers: a 429 on a browse is a slightly worse browse, a 429 on `ready` or
        // `delivered` strands an order somebody is waiting on.
        shedBrowse.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        lifecycle.StatusCode.Should().Be(HttpStatusCode.OK);

        (await parked).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Disabled_Should_LeaveEveryRequestUnlimited()
    {
        // Arrange — the kill switch has to actually remove the middleware, not merely configure it
        // generously. A limiter that is "off" but still in the pipeline is a latent 429.
        await using Edge edge = await Edge.StartAsync(new EdgeRateLimitingOptions
        {
            Enabled = false,
            ReadPermitLimit = 1,
            WindowSeconds = 30,
            GlobalConcurrencyLimit = 1,
        });

        // Act
        HttpResponseMessage first = await edge.GetAsync(ReadPath);
        HttpResponseMessage second = await edge.GetAsync(ReadPath);
        HttpResponseMessage third = await edge.GetAsync(ReadPath);

        // Assert
        first.StatusCode.Should().Be(HttpStatusCode.OK);
        second.StatusCode.Should().Be(HttpStatusCode.OK);
        third.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    /// <summary>
    /// A minimal host carrying the real limiter in front of stand-in endpoints on the Gateway's
    /// public paths. The endpoints do nothing — what is under test is what happens in front of them.
    /// </summary>
    private sealed class Edge : IAsyncDisposable
    {
        private readonly WebApplication _app;
        private readonly HttpClient _client;

        // Released by default: only the concurrency test wants a request to sit in its handler, and
        // every other test would otherwise deadlock waiting for a gate nobody opens.
        private TaskCompletionSource _release = Released();
        private TaskCompletionSource _inFlight = Unstarted();

        private Edge(WebApplication app, HttpClient client)
        {
            _app = app;
            _client = client;
        }

        public static async Task<Edge> StartAsync(EdgeRateLimitingOptions options)
        {
            WebApplicationBuilder builder = WebApplication.CreateBuilder();

            builder.Logging.ClearProviders();
            builder.Configuration.AddInMemoryCollection(Settings(options));

            builder.Services.AddEdgeRateLimiting(
                builder.Configuration,
                _ => new InMemoryRateLimitStore());

            WebApplication app = builder.Build();
            Edge edge = null!;

            // Port 0: the OS picks a free one, so two of these can run side by side and neither
            // collides with whatever else is listening on this machine.
            app.Urls.Add("http://127.0.0.1:0");

            app.UseEdgeRateLimiting();

            // The read parks until released, so a test can hold the single concurrency slot open
            // and observe what the limiter does to everything arriving behind it.
            app.MapGet(ReadPath, async () =>
            {
                edge.MarkInFlight();

                await edge.Parked;

                return Results.Ok();
            });

            app.MapGet("/health/live", () => Results.Ok());
            app.MapGet("/health/ready", () => Results.Ok());
            app.MapGet("/hubs/tracking/negotiate", () => Results.Ok());
            app.MapPost(CriticalPath, () => Results.Ok());

            await app.StartAsync();

            var client = new HttpClient { BaseAddress = new Uri(app.Urls.First()) };

            edge = new Edge(app, client);

            return edge;
        }

        private Task Parked => _release.Task;

        public Task<HttpResponseMessage> GetAsync(string path) => _client.GetAsync(path);

        public Task<HttpResponseMessage> PostAsync(string path) => _client.PostAsync(path, content: null);

        /// <summary>Makes the next read sit in its handler, holding a concurrency permit, until released.</summary>
        public void ParkRequests()
        {
            _inFlight = Unstarted();
            _release = Unstarted();
        }

        /// <summary>
        /// Blocks until a request is actually inside its handler — the point at which it is holding a
        /// concurrency permit. Waiting on a timer instead is how a test like this becomes flaky.
        /// </summary>
        public Task WaitForRequestInFlightAsync() => _inFlight.Task;

        public void ReleaseParkedRequests() => _release.TrySetResult();

        private void MarkInFlight() => _inFlight.TrySetResult();

        public async ValueTask DisposeAsync()
        {
            ReleaseParkedRequests();

            _client.Dispose();

            await _app.StopAsync();
            await _app.DisposeAsync();
        }

        private static TaskCompletionSource Unstarted() => new(TaskCreationOptions.RunContinuationsAsynchronously);

        private static TaskCompletionSource Released()
        {
            TaskCompletionSource source = Unstarted();

            source.SetResult();

            return source;
        }

        /// <summary>
        /// The options as configuration keys, because that is how the Gateway supplies them and a
        /// test that bypassed the binding would not notice a renamed key.
        /// </summary>
        private static Dictionary<string, string?> Settings(EdgeRateLimitingOptions options)
        {
            const string section = EdgeRateLimitingOptions.SectionName;

            CultureInfo culture = CultureInfo.InvariantCulture;

            return new Dictionary<string, string?>
            {
                [$"{section}:{nameof(options.Enabled)}"] = options.Enabled.ToString(),
                [$"{section}:{nameof(options.GlobalConcurrencyLimit)}"] = options.GlobalConcurrencyLimit.ToString(culture),
                [$"{section}:{nameof(options.GlobalQueueLimit)}"] = options.GlobalQueueLimit.ToString(culture),
                [$"{section}:{nameof(options.WindowSeconds)}"] = options.WindowSeconds.ToString(culture),
                [$"{section}:{nameof(options.ReadPermitLimit)}"] = options.ReadPermitLimit.ToString(culture),
                [$"{section}:{nameof(options.WritePermitLimit)}"] = options.WritePermitLimit.ToString(culture),
                [$"{section}:{nameof(options.CriticalPermitLimit)}"] = options.CriticalPermitLimit.ToString(culture),
            };
        }
    }
}

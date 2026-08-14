using System.Globalization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace FoodDeliveryService.Common.Presentation.RateLimiting;

/// <summary>
/// Admission control for the single public entry point — Feature 3.5 Milestone G.
/// <para>
/// <b>Edge only.</b> Exactly one host calls this, and that is the design rather than an accident of
/// who got there first: limiting is a property of the boundary between the platform and the world,
/// and a limiter repeated on every hop multiplies its own limit and shreds internal traffic that has
/// already been admitted. Hard Rule 10 says all external traffic goes through the Gateway, which is
/// what makes one limiter sufficient.
/// </para>
/// <para>
/// It lives in <c>Common.Presentation</c> for the same reason the health probes and
/// <c>AddHostTelemetry</c> do: the Gateway takes no <c>Common.Infrastructure</c> dependency — it is a
/// proxy, not a module host — so anything it needs that is more than one line has to be reachable
/// from here. The one piece that genuinely belongs to infrastructure, the Redis-backed
/// <see cref="IRateLimitStore"/>, stays in the Gateway and is handed in.
/// </para>
/// <para>Two limits, doing two different jobs:</para>
/// <list type="number">
/// <item><description><b>A global concurrency limit</b> — the one that makes throughput plateau
/// instead of collapse past the knee. It is about the platform's total capacity, not about any one
/// caller.</description></item>
/// <item><description><b>A per-client fixed window</b>, partitioned by subject or IP
/// (<see cref="RateLimitClient"/>) and sized per <see cref="RateLimitTier"/> — the one that stops a
/// single client from being everyone's problem.</description></item>
/// </list>
/// <para>
/// Both are expressed through ASP.NET Core's built-in rate limiter rather than a bespoke middleware,
/// so rejections carry <c>Retry-After</c>, land on the framework's <c>aspnetcore.rate_limiting.*</c>
/// meter, and hold their lease for the request's real lifetime.
/// </para>
/// </summary>
public static class EdgeRateLimitingExtensions
{
    /// <summary>
    /// A partition key nothing is counted against — the exempt paths, and the critical tier's bypass
    /// of the global concurrency limit.
    /// </summary>
    private const string NoPartition = "none";

    /// <summary>
    /// The single shared partition every non-exempt, non-critical request is counted against. One
    /// constant key means one <see cref="ConcurrencyLimiter"/> instance for the whole process, which
    /// is what makes it a *global* limit rather than a per-client one.
    /// </summary>
    private const string GlobalPartition = "global";

    private const string ProblemContentType = "application/problem+json";

    private const string LoggerCategory = "FoodDeliveryService.RateLimiting";

    /// <summary>
    /// Pre-rendered, because the rejection path has to be cheaper than the request it is refusing —
    /// serializing a fresh <c>ProblemDetails</c> per shed request would make shedding a load of its
    /// own at exactly the moment the platform has none to spare. The correlation id is not in the
    /// body because <c>UseRequestCorrelation()</c> already echoes it on the response header.
    /// </summary>
    private const string ProblemBody =
        """
        {"type":"https://tools.ietf.org/html/rfc6585#section-4","title":"Too Many Requests","status":429,"detail":"The gateway is shedding load to stay available. Retry after the interval in the Retry-After header."}
        """;

    /// <summary>
    /// Registers the edge limiter. Pair with <see cref="UseEdgeRateLimiting"/>, which must be placed
    /// <b>after</b> <c>UseAuthentication()</c> — see <see cref="RateLimitClient"/> for why.
    /// </summary>
    /// <param name="services">The host's services.</param>
    /// <param name="configuration">Read for the <c>"RateLimiting"</c> section.</param>
    /// <param name="storeFactory">
    /// The shared counter. Required and explicit rather than defaulted: a limiter that silently
    /// falls back to a process-local store enforces N× its configured limit on N replicas and never
    /// mentions it, so choosing the store is the caller's decision to make out loud.
    /// </param>
    public static IServiceCollection AddEdgeRateLimiting(
        this IServiceCollection services,
        IConfiguration configuration,
        Func<IServiceProvider, IRateLimitStore> storeFactory)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(storeFactory);

        var options = new EdgeRateLimitingOptions();

        configuration.GetSection(EdgeRateLimitingOptions.SectionName).Bind(options);

        services.AddSingleton(options);
        services.AddSingleton(storeFactory);

        if (!options.Enabled)
        {
            return services;
        }

        services.AddRateLimiter(limiter =>
        {
            // Chained: a request must satisfy both, and the framework releases whatever it acquired
            // if the other refuses. Order is deliberate — the cheap in-process concurrency check
            // first, so an overloaded platform does not spend a Redis round trip per shed request.
            limiter.GlobalLimiter = PartitionedRateLimiter.CreateChained(
                GlobalConcurrencyLimiter(options),
                PerClientLimiter(options));

            // 503 is the framework default and it is the wrong code: "too many requests" is what
            // happened, and only 429 tells a client the request was *its* to slow down.
            limiter.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

            limiter.OnRejected = (context, cancellationToken) => Reject(context, options, cancellationToken);
        });

        return services;
    }

    /// <summary>
    /// Adds the limiter to the pipeline, and says in the startup log exactly what it will do — the
    /// limits, and whether they are shared across replicas or merely local.
    /// </summary>
    public static IApplicationBuilder UseEdgeRateLimiting(this IApplicationBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var options = app.ApplicationServices.GetRequiredService<EdgeRateLimitingOptions>();
        ILogger logger = app.ApplicationServices.GetRequiredService<ILoggerFactory>().CreateLogger(LoggerCategory);

        if (!options.Enabled)
        {
            logger.LogWarning(
                "Edge rate limiting is DISABLED. The gateway will queue past its capacity instead of " +
                "shedding, which is the pre-Milestone-G behaviour: past the knee every client times " +
                "out and no request is served rather than most requests being served.");

            return app;
        }

        // Resolved once here rather than per request, so a host that forgot to supply a store fails
        // at startup with this message instead of turning every request into a 500.
        IRateLimitStore store = app.ApplicationServices.GetRequiredService<IRateLimitStore>();

        if (store is InMemoryRateLimitStore)
        {
            logger.LogWarning(
                "The rate-limit counters are IN-MEMORY: the per-client limits are per process and " +
                "are multiplied by the replica count. Development only — point " +
                "'ConnectionStrings:Cache' at Redis to share them.");
        }

        logger.LogInformation(
            "Edge rate limiting is on ({Store}): global concurrency {Concurrency} (queue {Queue}), " +
            "per client per {Window}s — read {Read}, write {Write}, critical {Critical}. " +
            "Health and hub paths are exempt; the critical tier bypasses the concurrency limit.",
            store.GetType().Name,
            options.GlobalConcurrencyLimit,
            options.GlobalQueueLimit,
            options.WindowSeconds,
            options.ReadPermitLimit,
            options.WritePermitLimit,
            options.CriticalPermitLimit);

        return app.UseRateLimiter();
    }

    /// <summary>
    /// The platform's total admission control.
    /// <para>
    /// <see cref="RateLimitTier.Critical"/> bypasses it, and that bypass <b>is</b> the shaped
    /// shedding the milestone asks for: when the Gateway runs out of capacity, browsing and new
    /// orders are refused while a driver marking a delivery <c>delivered</c> still gets through. A
    /// limiter that sheds uniformly would take the deliveries down with the browse traffic, and the
    /// deliveries are the ones with a customer standing at a door.
    /// </para>
    /// </summary>
    private static PartitionedRateLimiter<HttpContext> GlobalConcurrencyLimiter(EdgeRateLimitingOptions options) =>
        PartitionedRateLimiter.Create<HttpContext, string>(context =>
        {
            RateLimitTier tier = RateLimitRoutePolicy.Classify(context.Request);

            if (tier is RateLimitTier.Exempt or RateLimitTier.Critical)
            {
                return RateLimitPartition.GetNoLimiter(NoPartition);
            }

            return RateLimitPartition.GetConcurrencyLimiter(
                GlobalPartition,
                _ => new ConcurrencyLimiterOptions
                {
                    PermitLimit = options.GlobalConcurrencyLimit,
                    QueueLimit = options.GlobalQueueLimit,
                    QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                });
        });

    /// <summary>One fixed window per <c>{prefix}:{tier}:{client}</c>, counted in the shared store.</summary>
    private static PartitionedRateLimiter<HttpContext> PerClientLimiter(EdgeRateLimitingOptions options) =>
        PartitionedRateLimiter.Create<HttpContext, string>(context =>
        {
            RateLimitTier tier = RateLimitRoutePolicy.Classify(context.Request);

            if (tier is RateLimitTier.Exempt)
            {
                return RateLimitPartition.GetNoLimiter(NoPartition);
            }

            // The tier is part of the key, not just the budget: a client that has exhausted its read
            // budget by browsing must still be able to complete a delivery, which it cannot do if
            // both share one counter.
            string partition = string.Create(
                CultureInfo.InvariantCulture,
                $"{options.KeyPrefix}:{Name(tier)}:{RateLimitClient.Resolve(context)}");

            IRateLimitStore store = context.RequestServices.GetRequiredService<IRateLimitStore>();

            return RateLimitPartition.Get(
                partition,
                key => new StoreRateLimiter(store, key, options.PermitLimitFor(tier), options.Window));
        });

    private static ValueTask Reject(
        OnRejectedContext context,
        EdgeRateLimitingOptions options,
        CancellationToken cancellationToken)
    {
        HttpResponse response = context.HttpContext.Response;

        if (response.HasStarted)
        {
            return ValueTask.CompletedTask;
        }

        response.StatusCode = StatusCodes.Status429TooManyRequests;

        // The per-client window knows exactly when it rolls over; a concurrency rejection does not,
        // and the honest answer there is "very soon" — the slot frees when the request in front of
        // you finishes. Both are better than the nothing a client gets from a dropped connection,
        // which it can only respond to by retrying immediately and making things worse.
        TimeSpan retryAfter = context.Lease.TryGetMetadata(MetadataName.RetryAfter, out TimeSpan lease)
            ? lease
            : TimeSpan.FromSeconds(options.DefaultRetryAfterSeconds);

        response.Headers.RetryAfter = Math.Max(1, (int)Math.Ceiling(retryAfter.TotalSeconds))
            .ToString(CultureInfo.InvariantCulture);

        response.ContentType = ProblemContentType;

        return new ValueTask(response.WriteAsync(ProblemBody, cancellationToken));
    }

    /// <summary>Lower-case tier name for the key. `ToString()` per request would allocate; this does not.</summary>
    private static string Name(RateLimitTier tier) => tier switch
    {
        RateLimitTier.Critical => "critical",
        RateLimitTier.Write => "write",
        RateLimitTier.Read => "read",
        _ => "exempt",
    };
}

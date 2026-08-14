using FoodDeliveryService.Common.Presentation.RateLimiting;
using StackExchange.Redis;

namespace FoodDeliveryService.Gateway.RateLimiting;

/// <summary>
/// The Gateway's half of the edge limiter: choosing where the counter lives.
/// <para>
/// Everything else — the tiers, the partitions, the two limits, the <c>429</c> — is shared code in
/// <c>Common.Presentation/RateLimiting</c>. What is here is one Redis connection, and it is a
/// deliberate, explicit handful of lines rather than a reach for <c>AddInfrastructure</c>: the
/// Gateway takes no <c>Common.Infrastructure</c> dependency (it is a proxy, not a module host), so it
/// builds the one thing it needs and nothing else.
/// </para>
/// </summary>
internal static class GatewayRateLimitingExtensions
{
    /// <summary>The connection string every Redis consumer on the platform reads.</summary>
    private const string CacheConnectionStringName = "Cache";

    /// <summary>
    /// Wires the limiter, backed by the shared Redis when one is configured.
    /// </summary>
    /// <param name="allowInMemoryFallback">
    /// Development only, and passed by the host rather than decided here — the same switch the cache
    /// and the distributed lock take (<c>docs/caching.md</c>). Outside Development an absent Redis is
    /// a startup failure: a per-process limiter enforces N× its configured limit on N replicas and
    /// would not say so, and a guardrail nobody can trust is worse than one nobody has.
    /// </param>
    public static IServiceCollection AddGatewayRateLimiting(
        this IServiceCollection services,
        IConfiguration configuration,
        bool allowInMemoryFallback)
    {
        IConfigurationSection section = configuration.GetSection(EdgeRateLimitingOptions.SectionName);
        string? connectionString = configuration.GetConnectionString(CacheConnectionStringName);

        // Read before the connection is opened, so turning the limiter off does not still require a
        // Redis to be reachable — or, outside Development, throw over a store nothing will consult.
        bool enabled = section.GetValue(nameof(EdgeRateLimitingOptions.Enabled), true);

        if (!enabled || string.IsNullOrWhiteSpace(connectionString))
        {
            if (enabled && !allowInMemoryFallback)
            {
                throw new InvalidOperationException(
                    "The gateway rate limiter has no Redis connection ('ConnectionStrings:Cache'). " +
                    "Outside Development the in-memory fallback is refused: it is per process, so N " +
                    "gateway replicas would enforce N times the configured limit without reporting " +
                    "it. Configure Redis, or set 'RateLimiting:Enabled' to false and accept that the " +
                    "edge has no admission control at all.");
            }

            // `UseEdgeRateLimiting` is what says so in the startup log — including the warning that
            // an in-memory store is per process. Nothing is logged here because the host's logging
            // pipeline does not exist yet at this point in `Program.cs`.
            return services.AddEdgeRateLimiting(configuration, _ => new InMemoryRateLimitStore());
        }

        IConnectionMultiplexer multiplexer = ConnectionMultiplexer.Connect(Options(connectionString));

        services.AddSingleton(multiplexer);

        return services.AddEdgeRateLimiting(
            configuration,
            provider => new RedisRateLimitStore(
                provider.GetRequiredService<IConnectionMultiplexer>(),
                provider.GetRequiredService<ILogger<RedisRateLimitStore>>()));
    }

    /// <summary>
    /// The two hardening defaults, a knowing echo of
    /// <c>Common.Infrastructure/Caching/RedisConnectionOptions.Create</c> — which this project cannot
    /// call, by design. Duplicated rather than shared because copying two settings is cheaper than
    /// dragging the module-host infrastructure into a reverse proxy; if that file's defaults ever
    /// change, this is the other place to change.
    /// <list type="bullet">
    /// <item><description><c>AbortOnConnectFail = false</c> — an unreachable Redis must not stop the
    /// single public entry point from starting. The store fails open instead
    /// (<see cref="RedisRateLimitStore"/>).</description></item>
    /// <item><description>An exponential reconnect back-off, so a fleet of gateway pods that lost the
    /// same node does not retry in lockstep.</description></item>
    /// </list>
    /// </summary>
    private static ConfigurationOptions Options(string connectionString)
    {
        var options = ConfigurationOptions.Parse(connectionString);

        options.AbortOnConnectFail = false;
        options.ReconnectRetryPolicy = new ExponentialRetry(
            (int)TimeSpan.FromSeconds(1).TotalMilliseconds,
            (int)TimeSpan.FromSeconds(30).TotalMilliseconds);
        // Identifies the gateway in Redis' own CLIENT LIST, next to the six module hosts already
        // there — so a connection count that looks wrong can be traced to a service.
        options.ClientName = OpenTelemetry.DiagnosticsConfig.ServiceName;

        return options;
    }
}

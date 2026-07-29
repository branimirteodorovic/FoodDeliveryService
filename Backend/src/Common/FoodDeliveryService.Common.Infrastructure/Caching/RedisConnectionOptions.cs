using StackExchange.Redis;

namespace FoodDeliveryService.Common.Infrastructure.Caching;

/// <summary>
/// Turns the <c>"Cache"</c> connection string into hardened StackExchange.Redis
/// <see cref="ConfigurationOptions"/>. Every Redis consumer in a host builds its connection from
/// here — the distributed cache, the distributed lock, Delivery's driver-location GEO store and the
/// Real-Time SignalR backplane — so a managed endpoint is configured once, not four times.
/// <para>
/// The connection string stays the only thing that changes between environments: local development
/// passes <c>fooddeliveryservice.redis:6379</c>, Azure passes the portal's
/// <c>{name}.redis.cache.windows.net:6380,password=…,ssl=True,abortConnect=False</c>. Anything the
/// connection string sets wins; this class only supplies the defaults StackExchange.Redis gets wrong
/// for a managed, network-partitioned cache (see <see cref="Create"/>).
/// </para>
/// </summary>
public static class RedisConnectionOptions
{
    /// <summary>
    /// Parses <paramref name="connectionString"/> and applies the two hardening defaults:
    /// <list type="bullet">
    /// <item><description><c>AbortOnConnectFail = false</c> — a cache that is briefly unreachable
    /// must not take the host down with it. Connecting then always succeeds and the multiplexer
    /// reconnects in the background, which is also what Azure Cache for Redis requires (its nodes
    /// are patched and failed over underneath you). It is forced rather than defaulted: the whole
    /// startup path in <c>AddInfrastructure</c> depends on <c>Connect</c> not throwing for an
    /// unreachable server.</description></item>
    /// <item><description>An exponential reconnect back-off instead of the default linear one, so a
    /// fleet of pods that lost the same failing node does not retry in lockstep.</description></item>
    /// </list>
    /// Timeouts, retry counts and keep-alive are deliberately left at the StackExchange.Redis
    /// defaults so a connection string can tune them per environment. TLS needs no handling here
    /// either: StackExchange.Redis recognises the Azure Cache DNS suffixes and turns it on itself —
    /// <c>RedisConnectionOptionsTests</c> pins that, since it is the library's behaviour and not
    /// ours.
    /// </summary>
    /// <param name="connectionString">The <c>"Cache"</c> connection string.</param>
    /// <param name="clientName">
    /// Identifies this host in Redis' own <c>CLIENT LIST</c> and in the Azure portal's connected-
    /// clients view — pass the OpenTelemetry service name so a noisy connection is traceable to a
    /// service.
    /// </param>
    public static ConfigurationOptions Create(string connectionString, string? clientName = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        var options = ConfigurationOptions.Parse(connectionString);

        options.AbortOnConnectFail = false;
        options.ReconnectRetryPolicy = new ExponentialRetry(
            (int)TimeSpan.FromSeconds(1).TotalMilliseconds,
            (int)TimeSpan.FromSeconds(30).TotalMilliseconds);

        if (!string.IsNullOrWhiteSpace(clientName))
        {
            options.ClientName = clientName;
        }

        return options;
    }
}

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace FoodDeliveryService.Common.Infrastructure.Caching;

/// <summary>
/// Registered by <c>AddInfrastructure</c> only when Redis was unreachable at startup and the host
/// allowed the in-process fallback (development). Silent degradation is the dangerous part of a
/// fallback — the cache still works, so nothing looks broken, while <c>IDistributedLock</c> has
/// quietly stopped being distributed. This makes it one loud line in Seq at boot.
/// </summary>
internal sealed class InMemoryFallbackWarning(ILogger<InMemoryFallbackWarning> logger) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        logger.LogWarning(
            "Redis was unreachable at startup: this host is running on an in-process cache and an " +
            "in-process lock. Cached reads are not shared between replicas and IDistributedLock " +
            "excludes callers inside this process only — never run like this outside development.");

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

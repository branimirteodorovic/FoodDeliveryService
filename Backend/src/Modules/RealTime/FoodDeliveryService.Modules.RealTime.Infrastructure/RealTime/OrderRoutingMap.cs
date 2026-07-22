using FoodDeliveryService.Common.Application.Caching;
using FoodDeliveryService.Modules.RealTime.Application.RealTime;

namespace FoodDeliveryService.Modules.RealTime.Infrastructure.RealTime;

/// <summary>
/// Redis-backed <see cref="IOrderRoutingMap"/> over the shared <see cref="ICacheService"/>. Keys are
/// namespaced <c>rt:order:{orderId}</c> and expire after <see cref="Ttl"/> — an order's active
/// window. This is ephemeral routing state, so a plain cache set (no locking, last-write-wins) is
/// exactly right; the next status event re-warms it if it is ever lost.
/// </summary>
internal sealed class OrderRoutingMap(ICacheService cacheService) : IOrderRoutingMap
{
    // Generous enough to cover an order from placed to delivered without needing a refresh strategy;
    // every status transition re-sets it anyway, so a live order's row never actually lapses.
    private static readonly TimeSpan Ttl = TimeSpan.FromHours(6);

    public Task SetAsync(Guid orderId, OrderRoutingEntry entry, CancellationToken cancellationToken = default) =>
        cacheService.SetAsync(CreateKey(orderId), entry, Ttl, cancellationToken);

    private static string CreateKey(Guid orderId) => $"rt:order:{orderId}";
}

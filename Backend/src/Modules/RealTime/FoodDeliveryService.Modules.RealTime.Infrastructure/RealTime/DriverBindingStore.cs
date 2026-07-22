using FoodDeliveryService.Common.Application.Caching;
using FoodDeliveryService.Modules.RealTime.Application.RealTime;

namespace FoodDeliveryService.Modules.RealTime.Infrastructure.RealTime;

/// <summary>
/// Redis-backed <see cref="IDriverBindingStore"/> over the shared <see cref="ICacheService"/>.
/// Two keys per binding: <c>rt:driver:{driverId}</c> (the binding itself, read by the location
/// subscriber on every position report) and a reverse index <c>rt:order-driver:{orderId}</c> (so
/// <see cref="UnbindAsync"/> can clear the binding from an order id alone — the events that end a
/// delivery don't all carry a driver id, notably <c>OrderCancelledIntegrationEvent</c>).
/// </summary>
internal sealed class DriverBindingStore(ICacheService cacheService) : IDriverBindingStore
{
    // Generous enough to cover a delivery window; every reassignment re-sets it anyway.
    private static readonly TimeSpan Ttl = TimeSpan.FromHours(6);

    public Task BindAsync(Guid driverId, Guid orderId, Guid customerId, CancellationToken cancellationToken = default) =>
        Task.WhenAll(
            cacheService.SetAsync(DriverKey(driverId), new DriverBinding(orderId, customerId), Ttl, cancellationToken),
            cacheService.SetAsync(OrderDriverKey(orderId), driverId, Ttl, cancellationToken));

    public Task<DriverBinding?> GetAsync(Guid driverId, CancellationToken cancellationToken = default) =>
        cacheService.GetAsync<DriverBinding>(DriverKey(driverId), cancellationToken);

    public async Task UnbindAsync(Guid orderId, CancellationToken cancellationToken = default)
    {
        Guid driverId = await cacheService.GetAsync<Guid>(OrderDriverKey(orderId), cancellationToken);

        if (driverId == Guid.Empty)
        {
            // Never bound (or already cleared) — nothing to do.
            return;
        }

        await Task.WhenAll(
            cacheService.RemoveAsync(DriverKey(driverId), cancellationToken),
            cacheService.RemoveAsync(OrderDriverKey(orderId), cancellationToken));
    }

    private static string DriverKey(Guid driverId) => $"rt:driver:{driverId}";

    private static string OrderDriverKey(Guid orderId) => $"rt:order-driver:{orderId}";
}

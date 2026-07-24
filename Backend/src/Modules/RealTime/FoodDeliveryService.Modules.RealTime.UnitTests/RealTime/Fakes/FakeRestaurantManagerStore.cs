using FoodDeliveryService.Modules.RealTime.Application.RealTime;

namespace FoodDeliveryService.Modules.RealTime.UnitTests.RealTime.Fakes;

/// <summary>
/// Hand-rolled <see cref="IRestaurantManagerStore"/> test double — an in-memory manager→restaurant
/// map a test seeds directly, so <c>TrackingHub</c> tests can exercise the "mapped" and "no replica
/// row yet" branches without a database.
/// </summary>
internal sealed class FakeRestaurantManagerStore : IRestaurantManagerStore
{
    private readonly Dictionary<Guid, Guid> _restaurantIdByManagerId = [];

    public void Seed(Guid managerUserId, Guid restaurantId) => _restaurantIdByManagerId[managerUserId] = restaurantId;

    public Task UpsertAsync(Guid managerUserId, Guid restaurantId, string restaurantName, CancellationToken cancellationToken = default)
    {
        _restaurantIdByManagerId[managerUserId] = restaurantId;
        return Task.CompletedTask;
    }

    public Task UpdateRestaurantNameAsync(Guid restaurantId, string restaurantName, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;

    public Task<Guid?> GetRestaurantIdAsync(Guid managerUserId, CancellationToken cancellationToken = default) =>
        Task.FromResult<Guid?>(_restaurantIdByManagerId.TryGetValue(managerUserId, out Guid restaurantId) ? restaurantId : null);
}

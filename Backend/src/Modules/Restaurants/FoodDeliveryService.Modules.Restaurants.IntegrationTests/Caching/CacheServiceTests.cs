using AwesomeAssertions;
using FoodDeliveryService.Common.Application.Caching;
using FoodDeliveryService.Modules.Restaurants.IntegrationTests.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace FoodDeliveryService.Modules.Restaurants.IntegrationTests.Caching;

/// <summary>
/// Round-trips <see cref="ICacheService.GetOrCreateAsync{T}"/> through the real Redis testcontainer
/// (see <see cref="IntegrationTestWebAppFactory"/>) — the Common cache-aside helper (Milestone A of
/// the caching plan) has no dedicated integration suite of its own, so it borrows Restaurants',
/// which already wires a live Redis container.
/// </summary>
public class CacheServiceTests : BaseIntegrationTest
{
    public CacheServiceTests(IntegrationTestWebAppFactory factory) : base(factory)
    {
    }

    [Fact]
    public async Task GetOrCreateAsync_Should_ServeSecondCallFromCache_WithoutReRunningFactory()
    {
        // Arrange
        await using AsyncServiceScope scope = Factory.Services.CreateAsyncScope();
        var cacheService = scope.ServiceProvider.GetRequiredService<ICacheService>();
        string key = $"caching-tests:{Guid.NewGuid()}";
        var factoryCallCount = 0;

        Task<string> LoadValue(CancellationToken cancellationToken)
        {
            factoryCallCount++;
            return Task.FromResult("redis-value");
        }

        // Act
        string first = await cacheService.GetOrCreateAsync(
            key,
            LoadValue,
            TimeSpan.FromMinutes(1),
            TestContext.Current.CancellationToken);

        string second = await cacheService.GetOrCreateAsync(
            key,
            LoadValue,
            TimeSpan.FromMinutes(1),
            TestContext.Current.CancellationToken);

        // Assert
        first.Should().Be("redis-value");
        second.Should().Be("redis-value");
        factoryCallCount.Should().Be(1);
    }

    [Fact]
    public async Task GetOrCreateAsync_Should_ReRunFactory_AfterTtlExpires()
    {
        // Arrange
        await using AsyncServiceScope scope = Factory.Services.CreateAsyncScope();
        var cacheService = scope.ServiceProvider.GetRequiredService<ICacheService>();
        string key = $"caching-tests:{Guid.NewGuid()}";
        var factoryCallCount = 0;

        Task<string> LoadValue(CancellationToken cancellationToken)
        {
            factoryCallCount++;
            return Task.FromResult($"value-{factoryCallCount}");
        }

        // Act — a 1s TTL (± jitter, comfortably under the 2s wait) must have evicted by the second call.
        string beforeExpiry = await cacheService.GetOrCreateAsync(
            key,
            LoadValue,
            TimeSpan.FromSeconds(1),
            TestContext.Current.CancellationToken);

        await Task.Delay(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);

        string afterExpiry = await cacheService.GetOrCreateAsync(
            key,
            LoadValue,
            TimeSpan.FromSeconds(1),
            TestContext.Current.CancellationToken);

        // Assert
        beforeExpiry.Should().Be("value-1");
        afterExpiry.Should().Be("value-2");
        factoryCallCount.Should().Be(2);
    }
}

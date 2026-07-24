using AwesomeAssertions;
using FoodDeliveryService.Common.Application.Caching;
using FoodDeliveryService.Common.Infrastructure.Caching;
using Microsoft.Extensions.DependencyInjection;

namespace FoodDeliveryService.Common.UnitTests.Caching;

public class CacheServiceTests
{
    // CachingSettings' own defaults (2 min TTL, 10% jitter) are used as-is — AddDistributedMemoryCache
    // registers the open-generic IOptions<> plumbing, so IOptions<CachingSettings> resolves without
    // any explicit Configure call.
    private readonly ICacheService _sut = new ServiceCollection()
        .AddDistributedMemoryCache()
        .AddSingleton<ICacheService, CacheService>()
        .BuildServiceProvider()
        .GetRequiredService<ICacheService>();

    [Fact]
    public async Task GetOrCreateAsync_Should_ReturnCachedValue_WithoutInvokingFactory_OnHit()
    {
        // Arrange
        string key = $"tests:{Guid.NewGuid()}";
        var factoryCallCount = 0;

        Task<string> LoadValue(CancellationToken cancellationToken)
        {
            factoryCallCount++;
            return Task.FromResult("value");
        }

        await _sut.GetOrCreateAsync(key, LoadValue, TimeSpan.FromMinutes(1), TestContext.Current.CancellationToken);

        // Act
        string result = await _sut.GetOrCreateAsync(
            key,
            LoadValue,
            TimeSpan.FromMinutes(1),
            TestContext.Current.CancellationToken);

        // Assert
        result.Should().Be("value");
        factoryCallCount.Should().Be(1);
    }

    [Fact]
    public async Task GetOrCreateAsync_Should_InvokeFactoryAndStoreResult_OnMiss()
    {
        // Arrange
        string key = $"tests:{Guid.NewGuid()}";
        var factoryCallCount = 0;

        Task<string> LoadValue(CancellationToken cancellationToken)
        {
            factoryCallCount++;
            return Task.FromResult("fresh-value");
        }

        // Act
        string result = await _sut.GetOrCreateAsync(
            key,
            LoadValue,
            TimeSpan.FromMinutes(1),
            TestContext.Current.CancellationToken);

        // Assert
        result.Should().Be("fresh-value");
        factoryCallCount.Should().Be(1);

        string? stored = await _sut.GetAsync<string>(key, TestContext.Current.CancellationToken);
        stored.Should().Be("fresh-value");
    }

    [Fact]
    public async Task GetOrCreateAsync_Should_NotStoreResult_WhenFactoryReturnsNull()
    {
        // Arrange
        string key = $"tests:{Guid.NewGuid()}";
        Func<CancellationToken, Task<string?>> loadNull = _ => Task.FromResult<string?>(null);

        // Act
        string? result = await _sut.GetOrCreateAsync(
            key,
            loadNull,
            TimeSpan.FromMinutes(1),
            TestContext.Current.CancellationToken);

        // Assert
        result.Should().BeNull();

        string? stored = await _sut.GetAsync<string>(key, TestContext.Current.CancellationToken);
        stored.Should().BeNull();
    }
}

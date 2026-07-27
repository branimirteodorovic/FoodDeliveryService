using AwesomeAssertions;
using FoodDeliveryService.Common.Application.Behaviors;
using FoodDeliveryService.Common.Application.Caching;
using FoodDeliveryService.Common.Domain;
using FoodDeliveryService.Common.Infrastructure.Caching;
using MediatR;
using Microsoft.Extensions.DependencyInjection;

namespace FoodDeliveryService.Common.UnitTests.Behaviors;

public class QueryCachingBehaviorTests
{
    // A real CacheService over AddDistributedMemoryCache — same approach Milestone A's
    // CacheServiceTests uses — so these tests exercise the real Get/Set round-trip rather than a
    // hand-rolled fake.
    private readonly ICacheService _cacheService = new ServiceCollection()
        .AddDistributedMemoryCache()
        .AddSingleton<ICacheService, CacheService>()
        .BuildServiceProvider()
        .GetRequiredService<ICacheService>();

    private sealed record CachedTestQuery(string CacheKey) : ICachedQuery
    {
        public TimeSpan? Expiration => TimeSpan.FromMinutes(1);
    }

    [Fact]
    public async Task Handle_Should_ReturnCachedValue_WithoutInvokingNext_OnHit()
    {
        // Arrange
        var behavior = new QueryCachingBehavior<CachedTestQuery, Result<string>>(_cacheService);
        var request = new CachedTestQuery($"tests:{Guid.NewGuid()}");
        var nextCallCount = 0;

        Task<Result<string>> Next(CancellationToken cancellationToken)
        {
            nextCallCount++;
            return Task.FromResult(Result.Success("value"));
        }

        await behavior.Handle(request, Next, TestContext.Current.CancellationToken);

        // Act
        Result<string> second = await behavior.Handle(request, Next, TestContext.Current.CancellationToken);

        // Assert
        second.Value.Should().Be("value");
        nextCallCount.Should().Be(1);
    }

    [Fact]
    public async Task Handle_Should_InvokeNextAndStoreResult_OnMiss()
    {
        // Arrange
        var behavior = new QueryCachingBehavior<CachedTestQuery, Result<string>>(_cacheService);
        var request = new CachedTestQuery($"tests:{Guid.NewGuid()}");

        // Act
        Result<string> result = await behavior.Handle(
            request,
            _ => Task.FromResult(Result.Success("fresh-value")),
            TestContext.Current.CancellationToken);

        // Assert
        result.Value.Should().Be("fresh-value");

        Result<string>? cached = await _cacheService.GetAsync<Result<string>>(
            request.CacheKey,
            TestContext.Current.CancellationToken);

        cached.Should().NotBeNull();
        cached!.Value.Should().Be("fresh-value");
    }

    [Fact]
    public async Task Handle_Should_NotCacheResult_WhenNextReturnsFailure()
    {
        // Arrange
        var behavior = new QueryCachingBehavior<CachedTestQuery, Result<string>>(_cacheService);
        var request = new CachedTestQuery($"tests:{Guid.NewGuid()}");
        var error = Error.NotFound("Test.NotFound", "not found");
        var nextCallCount = 0;

        Task<Result<string>> Next(CancellationToken cancellationToken)
        {
            nextCallCount++;
            return Task.FromResult(Result.Failure<string>(error));
        }

        // Act — called twice; if the failure had been cached, the second call would short-circuit.
        Result<string> first = await behavior.Handle(request, Next, TestContext.Current.CancellationToken);
        Result<string> second = await behavior.Handle(request, Next, TestContext.Current.CancellationToken);

        // Assert
        first.IsFailure.Should().BeTrue();
        second.IsFailure.Should().BeTrue();
        nextCallCount.Should().Be(2);
    }

    [Fact]
    public async Task Pipeline_Should_SkipCachingBehavior_ForRequestsNotImplementingICachedQuery()
    {
        // Arrange — mirrors ApplicationConfiguration's real MediatR registration so the open-generic
        // constraint on QueryCachingBehavior<,> is exercised exactly as it is in production: a
        // request that doesn't implement ICachedQuery never gets this behavior in its pipeline.
        await using ServiceProvider provider = new ServiceCollection()
            .AddLogging()
            .AddSingleton(_cacheService)
            .AddMediatR(config =>
            {
                config.RegisterServicesFromAssemblyContaining<QueryCachingBehaviorTests>();
                config.AddOpenBehavior(typeof(QueryCachingBehavior<,>));
            })
            .BuildServiceProvider();

        ISender sender = provider.GetRequiredService<ISender>();

        // Act
        Result<string> first = await sender.Send(
            new PlainTestQuery(),
            TestContext.Current.CancellationToken);

        Result<string> second = await sender.Send(
            new PlainTestQuery(),
            TestContext.Current.CancellationToken);

        // Assert — each Send re-ran the handler, proving no caching behavior intercepted it.
        first.Value.Should().StartWith("handled-");
        second.Value.Should().StartWith("handled-");
        first.Value.Should().NotBe(second.Value);
    }

    private sealed record PlainTestQuery : IRequest<Result<string>>;

    private sealed class PlainTestQueryHandler : IRequestHandler<PlainTestQuery, Result<string>>
    {
        public Task<Result<string>> Handle(PlainTestQuery request, CancellationToken cancellationToken) =>
            Task.FromResult(Result.Success($"handled-{Guid.NewGuid()}"));
    }
}

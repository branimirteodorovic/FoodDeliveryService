using AwesomeAssertions;
using FoodDeliveryService.Common.Application.Locking;
using FoodDeliveryService.Common.Infrastructure.Locking;

namespace FoodDeliveryService.Common.UnitTests.Locking;

/// <summary>
/// The <see cref="IDistributedLock"/> contract, exercised against the in-process implementation
/// (the fallback used when Redis is unreachable). These are the invariants the Redis
/// implementation is expected to match — its own round trip, including the Lua owner-check on
/// release, is covered by the Delivery integration suite against a real Redis container.
/// </summary>
public class DistributedLockTests
{
    private static readonly TimeSpan Ttl = TimeSpan.FromSeconds(30);

    private readonly InMemoryDistributedLock _sut = new();

    [Fact]
    public async Task TryAcquireAsync_Should_ReturnHandle_WhenTheResourceIsFree()
    {
        // Act
        await using IAsyncDisposable? handle = await _sut.TryAcquireAsync(
            Resource(),
            Ttl,
            TestContext.Current.CancellationToken);

        // Assert
        handle.Should().NotBeNull();
    }

    [Fact]
    public async Task TryAcquireAsync_Should_ReturnNull_WhenTheResourceIsAlreadyHeld()
    {
        // Arrange
        string resource = Resource();

        await using IAsyncDisposable? holder = await _sut.TryAcquireAsync(
            resource,
            Ttl,
            TestContext.Current.CancellationToken);
        holder.Should().NotBeNull();

        // Act
        await using IAsyncDisposable? contender = await _sut.TryAcquireAsync(
            resource,
            Ttl,
            TestContext.Current.CancellationToken);

        // Assert
        contender.Should().BeNull("the second caller must not enter a critical section someone else holds");
    }

    [Fact]
    public async Task TryAcquireAsync_Should_ReturnHandle_ForADifferentResource()
    {
        // Arrange
        await using IAsyncDisposable? holder = await _sut.TryAcquireAsync(
            Resource(),
            Ttl,
            TestContext.Current.CancellationToken);
        holder.Should().NotBeNull();

        // Act
        await using IAsyncDisposable? other = await _sut.TryAcquireAsync(
            Resource(),
            Ttl,
            TestContext.Current.CancellationToken);

        // Assert — locks are per resource; one driver's lock never blocks another's.
        other.Should().NotBeNull();
    }

    [Fact]
    public async Task DisposeAsync_Should_ReleaseTheResource()
    {
        // Arrange
        string resource = Resource();

        IAsyncDisposable? holder = await _sut.TryAcquireAsync(resource, Ttl, TestContext.Current.CancellationToken);
        holder.Should().NotBeNull();

        // Act
        await holder!.DisposeAsync();

        // Assert
        await using IAsyncDisposable? next = await _sut.TryAcquireAsync(
            resource,
            Ttl,
            TestContext.Current.CancellationToken);
        next.Should().NotBeNull("releasing the lock must let the next caller in");
    }

    [Fact]
    public async Task DisposeAsync_Should_BeIdempotent()
    {
        // Arrange
        string resource = Resource();

        IAsyncDisposable? holder = await _sut.TryAcquireAsync(resource, Ttl, TestContext.Current.CancellationToken);
        holder.Should().NotBeNull();

        await holder!.DisposeAsync();

        IAsyncDisposable? next = await _sut.TryAcquireAsync(resource, Ttl, TestContext.Current.CancellationToken);
        next.Should().NotBeNull();

        // Act — the first handle is disposed a second time (a using block after an explicit
        // release), which must not touch the lock the next caller now holds.
        await holder.DisposeAsync();

        // Assert
        await using IAsyncDisposable? contender = await _sut.TryAcquireAsync(
            resource,
            Ttl,
            TestContext.Current.CancellationToken);
        contender.Should().BeNull("the second holder is still inside its critical section");

        await next!.DisposeAsync();
    }

    [Fact]
    public async Task TryAcquireAsync_Should_TakeOverALapsedLock_AndTheLapsedHolderMustNotReleaseIt()
    {
        // Arrange — a holder whose TTL expires while it is still running (it crashed, or ran long).
        string resource = Resource();

        IAsyncDisposable? lapsed = await _sut.TryAcquireAsync(
            resource,
            TimeSpan.FromMilliseconds(20),
            TestContext.Current.CancellationToken);
        lapsed.Should().NotBeNull();

        await Task.Delay(TimeSpan.FromMilliseconds(100), TestContext.Current.CancellationToken);

        // Act — the TTL is what keeps a crashed holder from blocking the resource forever.
        IAsyncDisposable? next = await _sut.TryAcquireAsync(resource, Ttl, TestContext.Current.CancellationToken);
        next.Should().NotBeNull("an expired lock must not block progress");

        // The lapsed holder finishes and disposes — the owner check has to ignore it.
        await lapsed!.DisposeAsync();

        // Assert
        await using IAsyncDisposable? contender = await _sut.TryAcquireAsync(
            resource,
            Ttl,
            TestContext.Current.CancellationToken);
        contender.Should().BeNull("a lapsed holder must never release a lock another caller has since taken");

        await next!.DisposeAsync();
    }

    [Fact]
    public async Task TryAcquireAsync_Should_AdmitExactlyOneOfManyConcurrentCallers()
    {
        // Arrange
        string resource = Resource();

        // Act — twenty callers race for the same resource at once.
        IAsyncDisposable?[] handles = await Task.WhenAll(
            Enumerable.Range(0, 20).Select(_ => _sut.TryAcquireAsync(
                resource,
                Ttl,
                TestContext.Current.CancellationToken)));

        // Assert
        handles.Count(h => h is not null).Should().Be(1, "a lock admits a single winner");

        foreach (IAsyncDisposable handle in handles.OfType<IAsyncDisposable>())
        {
            await handle.DisposeAsync();
        }
    }

    private static string Resource() => $"tests:lock:{Guid.NewGuid()}";
}

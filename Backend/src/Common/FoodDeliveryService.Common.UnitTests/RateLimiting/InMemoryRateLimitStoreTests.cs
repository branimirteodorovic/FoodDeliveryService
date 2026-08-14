using AwesomeAssertions;
using FoodDeliveryService.Common.Presentation.RateLimiting;

namespace FoodDeliveryService.Common.UnitTests.RateLimiting;

/// <summary>
/// The fixed-window contract, exercised against the in-process store — the Development fallback.
/// These are the invariants the Redis implementation's Lua script is written to match; its own
/// round trip is a property of a live Redis and is not simulated here.
/// </summary>
public class InMemoryRateLimitStoreTests
{
    private static readonly TimeSpan Window = TimeSpan.FromSeconds(10);

    private readonly TestTimeProvider _time = new();

    [Fact]
    public async Task TryAcquireAsync_Should_AdmitUpToTheLimit()
    {
        // Arrange
        InMemoryRateLimitStore store = Store();

        // Act
        RateLimitDecision[] decisions =
        [
            await Acquire(store, permitLimit: 3),
            await Acquire(store, permitLimit: 3),
            await Acquire(store, permitLimit: 3),
        ];

        // Assert
        decisions.Should().OnlyContain(decision => decision.IsAdmitted);
    }

    [Fact]
    public async Task TryAcquireAsync_Should_RejectPastTheLimit_WithTheRemainingWindow()
    {
        // Arrange
        InMemoryRateLimitStore store = Store();

        await Acquire(store, permitLimit: 1);

        // Advance inside the window: the wait it reports has to be what is *left*, not the whole
        // window — a client told to wait 10 s when 6 s remain gives up more capacity than it needs.
        _time.Advance(TimeSpan.FromSeconds(4));

        // Act
        RateLimitDecision decision = await Acquire(store, permitLimit: 1);

        // Assert
        decision.IsAdmitted.Should().BeFalse();
        decision.RetryAfter.Should().Be(TimeSpan.FromSeconds(6));
    }

    [Fact]
    public async Task TryAcquireAsync_Should_AdmitAgain_OnceTheWindowHasRolledOver()
    {
        // Arrange
        InMemoryRateLimitStore store = Store();

        await Acquire(store, permitLimit: 1);
        (await Acquire(store, permitLimit: 1)).IsAdmitted.Should().BeFalse();

        // Act
        _time.Advance(Window);

        RateLimitDecision decision = await Acquire(store, permitLimit: 1);

        // Assert — a limiter that never forgives is a ban, not a guardrail.
        decision.IsAdmitted.Should().BeTrue();
    }

    [Fact]
    public async Task TryAcquireAsync_Should_CountEachKeySeparately()
    {
        // Arrange — one client exhausting its budget must not spend anyone else's, and a client that
        // has used up its reads must still be able to complete a delivery.
        InMemoryRateLimitStore store = Store();

        await Acquire(store, permitLimit: 1, key: "ratelimit:read:sub:first");

        // Act
        RateLimitDecision otherClient = await Acquire(store, permitLimit: 1, key: "ratelimit:read:sub:second");
        RateLimitDecision otherTier = await Acquire(store, permitLimit: 1, key: "ratelimit:critical:sub:first");

        // Assert
        otherClient.IsAdmitted.Should().BeTrue();
        otherTier.IsAdmitted.Should().BeTrue();
    }

    private InMemoryRateLimitStore Store() => new(_time);

    private static ValueTask<RateLimitDecision> Acquire(
        InMemoryRateLimitStore store,
        int permitLimit,
        string key = "ratelimit:read:sub:first") =>
        store.TryAcquireAsync(key, permitLimit, Window, TestContext.Current.CancellationToken);

    /// <summary>
    /// A hand-cranked clock. Windows are the one thing here that cannot be tested against a real one
    /// without sleeping through them, and a test suite that sleeps for ten seconds to prove a
    /// ten-second window is a test suite people stop running.
    /// </summary>
    private sealed class TestTimeProvider : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 8, 14, 12, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan delta) => _now = _now.Add(delta);
    }
}

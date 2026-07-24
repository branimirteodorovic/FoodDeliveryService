using AwesomeAssertions;
using FoodDeliveryService.Common.Infrastructure.Caching;

namespace FoodDeliveryService.Common.UnitTests.Caching;

public class CachingSettingsTests
{
    [Fact]
    public void ApplyJitter_Should_StayWithinConfiguredBand()
    {
        // Arrange
        var settings = new CachingSettings { JitterPercentage = 0.10 };
        var baseExpiration = TimeSpan.FromMinutes(2);
        var lowerBound = TimeSpan.FromTicks((long)(baseExpiration.Ticks * 0.90));
        var upperBound = TimeSpan.FromTicks((long)(baseExpiration.Ticks * 1.10));

        // Act & Assert — jitter is randomized, so sample repeatedly to catch out-of-band values.
        for (var i = 0; i < 200; i++)
        {
            TimeSpan jittered = settings.ApplyJitter(baseExpiration);

            jittered.Should().BeGreaterThanOrEqualTo(lowerBound);
            jittered.Should().BeLessThanOrEqualTo(upperBound);
        }
    }

    [Fact]
    public void ApplyJitter_Should_ReturnExactExpiration_WhenJitterPercentageIsZero()
    {
        // Arrange
        var settings = new CachingSettings { JitterPercentage = 0 };
        var baseExpiration = TimeSpan.FromMinutes(5);

        // Act
        TimeSpan jittered = settings.ApplyJitter(baseExpiration);

        // Assert
        jittered.Should().Be(baseExpiration);
    }
}

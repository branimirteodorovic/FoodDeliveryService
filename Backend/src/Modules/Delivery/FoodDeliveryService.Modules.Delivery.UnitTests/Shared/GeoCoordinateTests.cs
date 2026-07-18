using AwesomeAssertions;
using FoodDeliveryService.Common.Domain;
using FoodDeliveryService.Modules.Delivery.Domain.Shared;
using FoodDeliveryService.Modules.Delivery.UnitTests.Abstractions;

namespace FoodDeliveryService.Modules.Delivery.UnitTests.Shared;

public class GeoCoordinateTests : BaseTest
{
    [Fact]
    public void Create_ShouldSucceed_ForCoordinatesInRange()
    {
        // Act
        Result<GeoCoordinate> result = GeoCoordinate.Create(44.8176, 20.4633);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Latitude.Should().Be(44.8176);
        result.Value.Longitude.Should().Be(20.4633);
    }

    [Theory]
    [InlineData(90, 180)]
    [InlineData(-90, -180)]
    [InlineData(0, 0)]
    public void Create_ShouldAcceptTheBoundaryValues(double latitude, double longitude)
    {
        // Act
        Result<GeoCoordinate> result = GeoCoordinate.Create(latitude, longitude);

        // Assert
        result.IsSuccess.Should().BeTrue();
    }

    [Theory]
    [InlineData(90.0001)]
    [InlineData(-90.0001)]
    [InlineData(1000)]
    [InlineData(double.NaN)]
    public void Create_ShouldFail_ForOutOfRangeLatitude(double latitude)
    {
        // Act
        Result<GeoCoordinate> result = GeoCoordinate.Create(latitude, 0);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(GeoCoordinateErrors.LatitudeOutOfRange);
    }

    [Theory]
    [InlineData(180.0001)]
    [InlineData(-180.0001)]
    [InlineData(1000)]
    [InlineData(double.NaN)]
    public void Create_ShouldFail_ForOutOfRangeLongitude(double longitude)
    {
        // Act
        Result<GeoCoordinate> result = GeoCoordinate.Create(0, longitude);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(GeoCoordinateErrors.LongitudeOutOfRange);
    }

    [Fact]
    public void DistanceKmTo_ShouldBeZero_ForTheSamePoint()
    {
        // Arrange
        GeoCoordinate point = GeoCoordinate.Create(51.5074, -0.1278).Value;

        // Act & Assert
        point.DistanceKmTo(point).Should().BeApproximately(0, 0.001);
    }

    [Theory]
    // London ↔ Paris ≈ 344 km
    [InlineData(51.5074, -0.1278, 48.8566, 2.3522, 344)]
    // New York ↔ Los Angeles ≈ 3936 km
    [InlineData(40.7128, -74.0060, 34.0522, -118.2437, 3936)]
    // Belgrade ↔ Novi Sad ≈ 70 km (short, delivery-relevant range)
    [InlineData(44.8176, 20.4633, 45.2671, 19.8335, 70)]
    public void DistanceKmTo_ShouldMatchKnownCityPairs(
        double lat1,
        double lon1,
        double lat2,
        double lon2,
        double expectedKm)
    {
        // Arrange
        GeoCoordinate from = GeoCoordinate.Create(lat1, lon1).Value;
        GeoCoordinate to = GeoCoordinate.Create(lat2, lon2).Value;

        // Act
        double distance = from.DistanceKmTo(to);

        // Assert — a haversine bug is invisible until assignment picks the wrong driver, so pin the
        // distance to the known great-circle value within ~1%.
        distance.Should().BeApproximately(expectedKm, expectedKm * 0.01);
    }

    [Fact]
    public void DistanceKmTo_ShouldBeSymmetric()
    {
        // Arrange
        GeoCoordinate a = GeoCoordinate.Create(44.8176, 20.4633).Value;
        GeoCoordinate b = GeoCoordinate.Create(45.2671, 19.8335).Value;

        // Act & Assert
        a.DistanceKmTo(b).Should().BeApproximately(b.DistanceKmTo(a), 0.001);
    }
}

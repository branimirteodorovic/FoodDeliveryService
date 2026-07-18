using FoodDeliveryService.Common.Domain;

namespace FoodDeliveryService.Modules.Delivery.Domain.Shared;

/// <summary>
/// A WGS-84 position. Construction goes through Create so an out-of-range coordinate can never
/// reach the location store or the assignment routine — Redis would happily accept one and the
/// resulting distances would be silently wrong.
/// </summary>
public sealed record GeoCoordinate
{
    // IUGG mean Earth radius. The haversine below assumes a sphere; at delivery-radius scale the
    // error against a proper ellipsoid model is well under the GPS noise on the input.
    private const double EarthRadiusKm = 6371.0088;

    private GeoCoordinate(double latitude, double longitude)
    {
        Latitude = latitude;
        Longitude = longitude;
    }

    public double Latitude { get; }

    public double Longitude { get; }

    public static Result<GeoCoordinate> Create(double latitude, double longitude)
    {
        // NaN fails every comparison, so it has to be rejected explicitly rather than by range.
        if (double.IsNaN(latitude) || latitude is < -90 or > 90)
        {
            return Result.Failure<GeoCoordinate>(GeoCoordinateErrors.LatitudeOutOfRange);
        }

        if (double.IsNaN(longitude) || longitude is < -180 or > 180)
        {
            return Result.Failure<GeoCoordinate>(GeoCoordinateErrors.LongitudeOutOfRange);
        }

        return new GeoCoordinate(latitude, longitude);
    }

    /// <summary>
    /// Great-circle distance in kilometres. Pure function — the assignment routine's "nearest
    /// driver" ordering rests entirely on it, and a bug here is invisible until the wrong driver
    /// gets the offer, so it is unit-tested against known city pairs.
    /// </summary>
    public double DistanceKmTo(GeoCoordinate other)
    {
        ArgumentNullException.ThrowIfNull(other);

        double latitudeDelta = ToRadians(other.Latitude - Latitude);
        double longitudeDelta = ToRadians(other.Longitude - Longitude);

        double a = Math.Sin(latitudeDelta / 2) * Math.Sin(latitudeDelta / 2) +
                   Math.Cos(ToRadians(Latitude)) * Math.Cos(ToRadians(other.Latitude)) *
                   Math.Sin(longitudeDelta / 2) * Math.Sin(longitudeDelta / 2);

        return 2 * EarthRadiusKm * Math.Asin(Math.Min(1, Math.Sqrt(a)));
    }

    private static double ToRadians(double degrees) => degrees * Math.PI / 180;
}

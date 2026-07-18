using FoodDeliveryService.Common.Domain;

namespace FoodDeliveryService.Modules.Delivery.Domain.Shared;

public static class GeoCoordinateErrors
{
    public static readonly Error LatitudeOutOfRange = Error.Problem(
        "GeoCoordinate.LatitudeOutOfRange",
        "Latitude must be a number between -90 and 90");

    public static readonly Error LongitudeOutOfRange = Error.Problem(
        "GeoCoordinate.LongitudeOutOfRange",
        "Longitude must be a number between -180 and 180");
}

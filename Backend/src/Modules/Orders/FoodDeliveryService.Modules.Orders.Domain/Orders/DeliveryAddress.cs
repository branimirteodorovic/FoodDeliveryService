using FoodDeliveryService.Common.Domain;

namespace FoodDeliveryService.Modules.Orders.Domain.Orders;

// Owned value object (EF OwnsOne on Order) — snapshotted at placement so later profile or address
// edits never rewrite where an already-placed order is going. The client app supplies the map pin,
// so the coordinates are required for the Delivery service to route to the dropoff. They remain
// nullable on the record for EF materialization, while the Create factory enforces their presence.
public sealed record DeliveryAddress(
    string Street,
    string City,
    string PostalCode,
    string Country,
    string? Notes,
    double? Latitude,
    double? Longitude)
{
    public static Result<DeliveryAddress> Create(
        string street,
        string city,
        string postalCode,
        string country,
        string? notes,
        double? latitude,
        double? longitude)
    {
        if (latitude is null || longitude is null)
        {
            return Result.Failure<DeliveryAddress>(OrderErrors.MissingCoordinates);
        }

        if (latitude is < -90 or > 90 || longitude is < -180 or > 180)
        {
            return Result.Failure<DeliveryAddress>(OrderErrors.InvalidCoordinates);
        }

        return new DeliveryAddress(street, city, postalCode, country, notes, latitude, longitude);
    }
}

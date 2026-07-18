using FoodDeliveryService.Common.Domain;

namespace FoodDeliveryService.Modules.Restaurants.Domain.Restaurants;

// Owned value object (EF OwnsOne on Restaurant). Latitude/longitude are required — a restaurant
// without coordinates can never be assigned a driver (Delivery Feature 2.1). They stay nullable on
// the record so EF can materialize the owned columns, but Create enforces their presence and range.
public sealed record Address(
    string Street,
    string City,
    string PostalCode,
    string Country,
    double? Latitude,
    double? Longitude)
{
    public static Result<Address> Create(
        string street,
        string city,
        string postalCode,
        string country,
        double? latitude,
        double? longitude)
    {
        if (latitude is null || longitude is null)
        {
            return Result.Failure<Address>(RestaurantErrors.MissingCoordinates);
        }

        if (latitude is < -90 or > 90 || longitude is < -180 or > 180)
        {
            return Result.Failure<Address>(RestaurantErrors.InvalidCoordinates);
        }

        return new Address(street, city, postalCode, country, latitude, longitude);
    }
}

namespace FoodDeliveryService.Modules.Restaurants.Domain.Restaurants;

// Owned value object (EF OwnsOne on Restaurant). Latitude/longitude are optional for now — they
// are scaffolding for the later delivery-zone work.
public sealed record Address(
    string Street,
    string City,
    string PostalCode,
    string Country,
    double? Latitude,
    double? Longitude);

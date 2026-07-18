using FoodDeliveryService.Common.Application.EventBus;

namespace FoodDeliveryService.Modules.Restaurants.IntegrationEvents;

/// <summary>
/// Published when a restaurant's address (including its coordinates) changes. Full snapshot so the
/// Delivery service can keep its restaurant replica — and therefore the pickup point used for
/// re-offers — current without calling back.
/// </summary>
public sealed class RestaurantAddressUpdatedIntegrationEvent : IntegrationEvent
{
    public RestaurantAddressUpdatedIntegrationEvent(
        Guid id,
        DateTime occurredOnUtc,
        Guid restaurantId,
        string name,
        string street,
        string city,
        string postalCode,
        string country,
        double latitude,
        double longitude)
        : base(id, occurredOnUtc)
    {
        RestaurantId = restaurantId;
        Name = name;
        Street = street;
        City = city;
        PostalCode = postalCode;
        Country = country;
        Latitude = latitude;
        Longitude = longitude;
    }

    public Guid RestaurantId { get; init; }

    public string Name { get; init; }

    public string Street { get; init; }

    public string City { get; init; }

    public string PostalCode { get; init; }

    public string Country { get; init; }

    public double Latitude { get; init; }

    public double Longitude { get; init; }
}

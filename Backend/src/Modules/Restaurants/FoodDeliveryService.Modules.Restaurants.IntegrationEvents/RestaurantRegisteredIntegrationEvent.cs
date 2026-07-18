using FoodDeliveryService.Common.Application.EventBus;

namespace FoodDeliveryService.Modules.Restaurants.IntegrationEvents;

/// <summary>
/// Published when an Administrator onboards a restaurant. Full snapshot — future consumers
/// (Orders for commission splits, Notifications, search) must never call back for data.
/// </summary>
public sealed class RestaurantRegisteredIntegrationEvent : IntegrationEvent
{
    public RestaurantRegisteredIntegrationEvent(
        Guid id,
        DateTime occurredOnUtc,
        Guid restaurantId,
        Guid managerUserId,
        string name,
        string cuisineType,
        string street,
        string city,
        string postalCode,
        string country,
        double latitude,
        double longitude,
        decimal commissionRate)
        : base(id, occurredOnUtc)
    {
        RestaurantId = restaurantId;
        ManagerUserId = managerUserId;
        Name = name;
        CuisineType = cuisineType;
        Street = street;
        City = city;
        PostalCode = postalCode;
        Country = country;
        Latitude = latitude;
        Longitude = longitude;
        CommissionRate = commissionRate;
    }

    public Guid RestaurantId { get; init; }

    public Guid ManagerUserId { get; init; }

    public string Name { get; init; }

    public string CuisineType { get; init; }

    public string Street { get; init; }

    public string City { get; init; }

    public string PostalCode { get; init; }

    public string Country { get; init; }

    public double Latitude { get; init; }

    public double Longitude { get; init; }

    public decimal CommissionRate { get; init; }
}

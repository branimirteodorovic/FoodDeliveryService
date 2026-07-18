using FoodDeliveryService.Common.Domain;

namespace FoodDeliveryService.Modules.Delivery.Domain.Restaurants;

/// <summary>
/// Local read-only replica of a restaurant, keyed by the Restaurants service's RestaurantId and
/// populated from RestaurantRegisteredIntegrationEvent / RestaurantAddressUpdatedIntegrationEvent.
/// Gives Delivery the pickup coordinates for a re-offer after an address change and a name for the
/// admin/support screen — without querying the Restaurants database (hard rule #5). As a projection
/// of state owned by another service it raises no domain events.
/// </summary>
public sealed class Restaurant : Entity
{
    private Restaurant()
    {
    }

    public Guid Id { get; private set; }

    public string Name { get; private set; }

    public double Latitude { get; private set; }

    public double Longitude { get; private set; }

    public static Restaurant Create(Guid restaurantId, string name, double latitude, double longitude)
    {
        return new Restaurant
        {
            Id = restaurantId,
            Name = name,
            Latitude = latitude,
            Longitude = longitude
        };
    }

    public void Update(string name, double latitude, double longitude)
    {
        Name = name;
        Latitude = latitude;
        Longitude = longitude;
    }
}

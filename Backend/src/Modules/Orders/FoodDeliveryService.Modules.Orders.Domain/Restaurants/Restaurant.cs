using FoodDeliveryService.Common.Domain;

namespace FoodDeliveryService.Modules.Orders.Domain.Restaurants;

/// <summary>
/// Local read-only replica of a restaurant, keyed by the Restaurants service's RestaurantId and
/// populated from RestaurantRegisteredIntegrationEvent. Gives Orders the manager for ownership
/// checks on status transitions, the name to denormalize onto orders, and the commission rate to
/// snapshot at placement — all without querying the Restaurants database (hard rule #5). As a
/// projection of state owned by another service it raises no domain events.
/// </summary>
public sealed class Restaurant : Entity
{
    private Restaurant()
    {
    }

    public Guid Id { get; private set; }

    public Guid ManagerUserId { get; private set; }

    public string Name { get; private set; }

    public decimal CommissionRate { get; private set; }

    // Snapshotted so the OrderReadyForPickup event can carry the pickup coordinates the Delivery
    // service needs, without Orders querying the Restaurants database (hard rule #5).
    public double Latitude { get; private set; }

    public double Longitude { get; private set; }

    public static Restaurant Create(
        Guid restaurantId,
        Guid managerUserId,
        string name,
        decimal commissionRate,
        double latitude,
        double longitude)
    {
        return new Restaurant
        {
            Id = restaurantId,
            ManagerUserId = managerUserId,
            Name = name,
            CommissionRate = commissionRate,
            Latitude = latitude,
            Longitude = longitude
        };
    }

    public void Update(Guid managerUserId, string name, decimal commissionRate, double latitude, double longitude)
    {
        ManagerUserId = managerUserId;
        Name = name;
        CommissionRate = commissionRate;
        Latitude = latitude;
        Longitude = longitude;
    }
}

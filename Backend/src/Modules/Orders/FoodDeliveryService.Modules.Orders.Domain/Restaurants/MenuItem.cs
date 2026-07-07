using FoodDeliveryService.Common.Domain;

namespace FoodDeliveryService.Modules.Orders.Domain.Restaurants;

/// <summary>
/// Local read-only replica of a menu item, keyed by the Restaurants service's MenuItemId and
/// populated from the menu integration events. This is the authoritative source for placement
/// pricing and availability — clients send item ids + quantities only, and every line is priced
/// from here (never from client input). As a projection of state owned by another service it
/// raises no domain events.
/// </summary>
public sealed class MenuItem : Entity
{
    private MenuItem()
    {
    }

    public Guid Id { get; private set; }

    public Guid RestaurantId { get; private set; }

    public string Name { get; private set; }

    public decimal Price { get; private set; }

    public bool IsAvailable { get; private set; }

    public static MenuItem Create(Guid menuItemId, Guid restaurantId, string name, decimal price, bool isAvailable)
    {
        return new MenuItem
        {
            Id = menuItemId,
            RestaurantId = restaurantId,
            Name = name,
            Price = price,
            IsAvailable = isAvailable
        };
    }

    public void Update(string name, decimal price, bool isAvailable)
    {
        Name = name;
        Price = price;
        IsAvailable = isAvailable;
    }

    public void SetAvailability(bool isAvailable)
    {
        IsAvailable = isAvailable;
    }
}

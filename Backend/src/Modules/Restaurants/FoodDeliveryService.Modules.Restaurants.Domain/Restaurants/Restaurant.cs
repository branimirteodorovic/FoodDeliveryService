using FoodDeliveryService.Common.Domain;

namespace FoodDeliveryService.Modules.Restaurants.Domain.Restaurants;

public sealed class Restaurant : Entity
{
    private Restaurant()
    {
    }

    public Guid Id { get; private set; }

    public static Restaurant Create(Guid id)
    {
        return new Restaurant
        {
            Id = id
        };
    }
}

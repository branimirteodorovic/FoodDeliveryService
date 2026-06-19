using FoodDeliveryService.Common.Domain;

namespace FoodDeliveryService.Modules.Orders.Domain.Orders;

public sealed class Order : Entity
{
    private Order()
    {
    }

    public Guid Id { get; private set; }

    public static Order Create(Guid id)
    {
        return new Order
        {
            Id = id
        };
    }
}

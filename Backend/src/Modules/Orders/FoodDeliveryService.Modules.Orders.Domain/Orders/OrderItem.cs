using FoodDeliveryService.Common.Domain;

namespace FoodDeliveryService.Modules.Orders.Domain.Orders;

/// <summary>
/// Child of the Order aggregate. Name and unit price are snapshots taken from the MenuItem replica
/// at placement time — the menu may change later, but a placed order's lines never do. Created only
/// through <see cref="Order.Place"/>; raises no events of its own.
/// </summary>
public sealed class OrderItem : Entity
{
    private OrderItem()
    {
    }

    public Guid Id { get; private set; }

    public Guid OrderId { get; private set; }

    public Guid MenuItemId { get; private set; }

    public string Name { get; private set; }

    public decimal UnitPrice { get; private set; }

    public int Quantity { get; private set; }

    public decimal LineTotal { get; private set; }

    internal static OrderItem Create(Guid orderId, OrderLine line)
    {
        return new OrderItem
        {
            Id = Guid.NewGuid(),
            OrderId = orderId,
            MenuItemId = line.MenuItemId,
            Name = line.Name,
            UnitPrice = line.UnitPrice,
            Quantity = line.Quantity,
            LineTotal = line.UnitPrice * line.Quantity
        };
    }
}

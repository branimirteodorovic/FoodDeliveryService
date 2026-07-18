using FoodDeliveryService.Common.Domain;
using FoodDeliveryService.Modules.Delivery.Domain.Shared;

namespace FoodDeliveryService.Modules.Delivery.Domain.Orders;

/// <summary>
/// Local read-only replica of an order that is ready for pickup, keyed by the Orders service's
/// OrderId and populated from OrderReadyForPickupIntegrationEvent. Gives Delivery the dropoff
/// address (incl. coordinates) and the customer for the tracking/support screen — without querying
/// the Orders database (hard rule #5). As a projection of state owned by another service it raises
/// no domain events.
/// </summary>
public sealed class Order : Entity
{
    private Order()
    {
    }

    public Guid Id { get; private set; }

    public Guid CustomerId { get; private set; }

    public Guid RestaurantId { get; private set; }

    public DeliveryAddress DeliveryAddress { get; private set; }

    public DateTime PlacedOnUtc { get; private set; }

    public static Order Create(
        Guid orderId,
        Guid customerId,
        Guid restaurantId,
        DeliveryAddress deliveryAddress,
        DateTime placedOnUtc)
    {
        return new Order
        {
            Id = orderId,
            CustomerId = customerId,
            RestaurantId = restaurantId,
            DeliveryAddress = deliveryAddress,
            PlacedOnUtc = placedOnUtc
        };
    }

    public void Update(Guid customerId, Guid restaurantId, DeliveryAddress deliveryAddress, DateTime placedOnUtc)
    {
        CustomerId = customerId;
        RestaurantId = restaurantId;
        DeliveryAddress = deliveryAddress;
        PlacedOnUtc = placedOnUtc;
    }
}

using FoodDeliveryService.Common.Application.EventBus;

namespace FoodDeliveryService.Modules.Orders.IntegrationEvents;

// Full snapshot consumed by the Delivery service to start driver assignment (Feature 2.1). Carries
// the pickup (restaurant) coordinates and the complete delivery address incl. coordinates so the
// assignment routine never calls back for data (hard rule #9).
public sealed class OrderReadyForPickupIntegrationEvent : IntegrationEvent
{
    public OrderReadyForPickupIntegrationEvent(
        Guid id,
        DateTime occurredOnUtc,
        Guid orderId,
        Guid customerId,
        Guid restaurantId,
        double restaurantLatitude,
        double restaurantLongitude,
        string deliveryStreet,
        string deliveryCity,
        string deliveryPostalCode,
        string deliveryCountry,
        string? deliveryNotes,
        double deliveryLatitude,
        double deliveryLongitude,
        decimal subtotal,
        DateTime placedOnUtc)
        : base(id, occurredOnUtc)
    {
        OrderId = orderId;
        CustomerId = customerId;
        RestaurantId = restaurantId;
        RestaurantLatitude = restaurantLatitude;
        RestaurantLongitude = restaurantLongitude;
        DeliveryStreet = deliveryStreet;
        DeliveryCity = deliveryCity;
        DeliveryPostalCode = deliveryPostalCode;
        DeliveryCountry = deliveryCountry;
        DeliveryNotes = deliveryNotes;
        DeliveryLatitude = deliveryLatitude;
        DeliveryLongitude = deliveryLongitude;
        Subtotal = subtotal;
        PlacedOnUtc = placedOnUtc;
    }

    public Guid OrderId { get; init; }

    public Guid CustomerId { get; init; }

    public Guid RestaurantId { get; init; }

    public double RestaurantLatitude { get; init; }

    public double RestaurantLongitude { get; init; }

    public string DeliveryStreet { get; init; }

    public string DeliveryCity { get; init; }

    public string DeliveryPostalCode { get; init; }

    public string DeliveryCountry { get; init; }

    public string? DeliveryNotes { get; init; }

    public double DeliveryLatitude { get; init; }

    public double DeliveryLongitude { get; init; }

    public decimal Subtotal { get; init; }

    public DateTime PlacedOnUtc { get; init; }
}

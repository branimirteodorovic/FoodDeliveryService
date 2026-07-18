namespace FoodDeliveryService.Modules.Orders.Application.Orders.MarkOrderReady;

public sealed record OrderPickupDetailsResponse(
    Guid OrderId,
    Guid CustomerId,
    Guid RestaurantId,
    double RestaurantLatitude,
    double RestaurantLongitude,
    string DeliveryStreet,
    string DeliveryCity,
    string DeliveryPostalCode,
    string DeliveryCountry,
    string? DeliveryNotes,
    double DeliveryLatitude,
    double DeliveryLongitude,
    decimal Subtotal,
    DateTime PlacedOnUtc);

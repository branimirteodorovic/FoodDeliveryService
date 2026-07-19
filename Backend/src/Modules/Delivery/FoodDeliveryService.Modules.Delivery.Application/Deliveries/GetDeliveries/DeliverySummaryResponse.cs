using FoodDeliveryService.Modules.Delivery.Domain.Deliveries;

namespace FoodDeliveryService.Modules.Delivery.Application.Deliveries.GetDeliveries;

// List projection — the tracking detail (coordinates, live position) is fetched per delivery via
// GetDelivery.
public sealed record DeliverySummaryResponse(
    Guid Id,
    Guid OrderId,
    Guid RestaurantId,
    Guid CustomerId,
    DeliveryStatus Status,
    Guid? DriverId,
    DateTime? AssignedOnUtc,
    DateTime? PickedUpOnUtc,
    DateTime? DeliveredOnUtc,
    DateTime CreatedOnUtc);

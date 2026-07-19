using FoodDeliveryService.Modules.Delivery.Domain.Deliveries;

namespace FoodDeliveryService.Modules.Delivery.Application.Deliveries.GetDelivery;

/// <summary>
/// The full delivery view for the tracking screen (Feature 2.2). Driver name is null until a driver
/// is assigned; the current driver position is read from the live location store (not the DB) and is
/// null once the delivery is terminal or the driver has gone stale.
/// </summary>
public sealed record DeliveryResponse(
    Guid Id,
    Guid OrderId,
    Guid RestaurantId,
    Guid CustomerId,
    DeliveryStatus Status,
    Guid? DriverId,
    string? DriverFirstName,
    string? DriverLastName,
    double PickupLatitude,
    double PickupLongitude,
    string DropoffStreet,
    string DropoffCity,
    string DropoffPostalCode,
    string DropoffCountry,
    string? DropoffNotes,
    double DropoffLatitude,
    double DropoffLongitude,
    DateTime? OfferExpiresOnUtc,
    DateTime? AssignedOnUtc,
    DateTime? PickedUpOnUtc,
    DateTime? DeliveredOnUtc,
    DateTime CreatedOnUtc,
    double? CurrentDriverLatitude,
    double? CurrentDriverLongitude,
    DateTime? CurrentDriverLocationRecordedOnUtc);

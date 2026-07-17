using FoodDeliveryService.Modules.Delivery.Domain.Drivers;

namespace FoodDeliveryService.Modules.Delivery.Application.Drivers.GetDriver;

// Response DTO — domain entities are never exposed in API responses.
public sealed record DriverResponse(
    Guid Id,
    string Email,
    string FirstName,
    string LastName,
    VehicleType VehicleType,
    DriverStatus Status,
    DateTime OnboardedOnUtc);

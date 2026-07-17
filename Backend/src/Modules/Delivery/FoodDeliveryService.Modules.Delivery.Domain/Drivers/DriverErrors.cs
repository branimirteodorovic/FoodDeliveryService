using FoodDeliveryService.Common.Domain;

namespace FoodDeliveryService.Modules.Delivery.Domain.Drivers;

public static class DriverErrors
{
    public static readonly Error NotOnboarded = Error.NotFound(
        "Drivers.NotOnboarded",
        "The calling user is not an onboarded driver");

    public static readonly Error AlreadyOnboarded = Error.Conflict(
        "Drivers.AlreadyOnboarded",
        "A driver profile already exists for this user");

    public static readonly Error NotSelf = Error.Problem(
        "Drivers.NotSelf",
        "Only the driver themselves (or an administrator) may access this driver's data");

    public static readonly Error InvalidVehicleType = Error.Problem(
        "Drivers.InvalidVehicleType",
        "The specified vehicle type is not supported");

    public static Error NotFound(Guid driverId) => Error.NotFound(
        "Drivers.NotFound",
        $"The driver with the identifier {driverId} was not found");
}

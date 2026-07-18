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

    // A Busy driver is mid-delivery. Going offline is refused outright rather than silently
    // orphaning the delivery — the driver finishes it or support reassigns it.
    public static readonly Error OnDelivery = Error.Problem(
        "Drivers.OnDelivery",
        "A driver cannot go offline while assigned to a delivery");

    // An Offline driver is not on duty, so their position is neither wanted nor trustworthy.
    public static readonly Error Offline = Error.Problem(
        "Drivers.Offline",
        "An offline driver cannot report a location");

    public static Error InvalidStatusTransition(DriverStatus from, DriverStatus to) => Error.Problem(
        "Drivers.InvalidStatusTransition",
        $"The driver cannot move from status {from} to status {to}");

    public static Error NotFound(Guid driverId) => Error.NotFound(
        "Drivers.NotFound",
        $"The driver with the identifier {driverId} was not found");
}

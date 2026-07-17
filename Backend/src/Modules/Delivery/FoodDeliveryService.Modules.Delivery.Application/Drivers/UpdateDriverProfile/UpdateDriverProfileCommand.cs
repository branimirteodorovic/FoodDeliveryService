using FoodDeliveryService.Common.Application.Messaging;

namespace FoodDeliveryService.Modules.Delivery.Application.Drivers.UpdateDriverProfile;

/// <summary>
/// The driver edits their own name/vehicle. Self-only by construction: the handler targets the
/// authenticated caller's driver profile (PUT delivery/drivers/me), never an arbitrary id.
/// </summary>
public sealed record UpdateDriverProfileCommand(
    string FirstName,
    string LastName,
    string VehicleType) : ICommand;

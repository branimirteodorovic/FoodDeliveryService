using FoodDeliveryService.Common.Application.Messaging;

namespace FoodDeliveryService.Modules.Delivery.Application.Drivers.OnboardDriver;

/// <summary>
/// Single admin action that onboards a driver: the handler provisions the invited DeliveryDriver
/// account in Users over the bus, then persists the Driver keyed by the returned UserId.
/// VehicleType is the enum name (e.g. "Car"), validated before parsing.
/// </summary>
public sealed record OnboardDriverCommand(
    string Email,
    string FirstName,
    string LastName,
    string VehicleType) : ICommand<Guid>;

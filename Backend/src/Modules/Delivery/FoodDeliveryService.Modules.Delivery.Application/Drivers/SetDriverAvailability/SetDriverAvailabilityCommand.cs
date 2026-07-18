using FoodDeliveryService.Common.Application.Messaging;

namespace FoodDeliveryService.Modules.Delivery.Application.Drivers.SetDriverAvailability;

/// <summary>
/// The authenticated driver clocks on or off. Targets the caller — there is no driver id to
/// tamper with.
/// </summary>
public sealed record SetDriverAvailabilityCommand(bool Available) : ICommand;

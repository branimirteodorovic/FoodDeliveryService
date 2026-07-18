using FoodDeliveryService.Common.Application.Messaging;

namespace FoodDeliveryService.Modules.Delivery.Application.Drivers.RecordDriverLocation;

/// <summary>
/// A position report from the authenticated driver's app, sent every few seconds. Targets the
/// caller — a driver can only report their own location.
/// </summary>
public sealed record RecordDriverLocationCommand(double Latitude, double Longitude) : ICommand;

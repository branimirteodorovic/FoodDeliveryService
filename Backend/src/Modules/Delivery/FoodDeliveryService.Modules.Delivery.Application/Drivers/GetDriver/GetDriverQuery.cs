using FoodDeliveryService.Common.Application.Messaging;

namespace FoodDeliveryService.Modules.Delivery.Application.Drivers.GetDriver;

/// <summary>
/// Reads a driver profile. A null <paramref name="DriverId"/> means "the caller's own profile"
/// (GET delivery/drivers/me). An explicit id is self-or-admin: a driver may read their own
/// profile; only an administrator (deliveries:administer) may read someone else's.
/// </summary>
public sealed record GetDriverQuery(Guid? DriverId) : IQuery<DriverResponse>;

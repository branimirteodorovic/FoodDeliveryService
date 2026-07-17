using FoodDeliveryService.Common.Application.Messaging;

namespace FoodDeliveryService.Modules.Delivery.Application.Drivers.SyncDriverFromUserProfile;

/// <summary>
/// Sent by the UserProfileUpdated integration event handler to keep the driver's name snapshot in
/// sync with the Users service. No-ops for users that are not drivers (customers, managers,
/// admins), so every profile update can be consumed safely.
/// </summary>
public sealed record SyncDriverFromUserProfileCommand(
    Guid UserId,
    string FirstName,
    string LastName) : ICommand;

using FoodDeliveryService.Common.Domain;

namespace FoodDeliveryService.Modules.Restaurants.Application.Abstractions.Provisioning;

/// <summary>
/// Abstraction over the synchronous bus calls to the Users service used during onboarding
/// (implemented in Infrastructure with MassTransit request/response, mirroring PermissionService,
/// so the Application layer stays free of messaging concerns).
/// </summary>
public interface IManagerProvisioningService
{
    /// <summary>
    /// Asks Users to create an invited RestaurantManager account (no password; activation token
    /// emailed by Notifications) and returns the new UserId, or the failure (e.g. duplicate email).
    /// </summary>
    Task<Result<Guid>> ProvisionManagerAsync(
        string email,
        string firstName,
        string lastName,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Compensation for a partial onboarding failure: asks Users to remove the just-provisioned,
    /// still-unactivated manager account so no orphan invited account remains.
    /// </summary>
    Task<Result> DeactivateManagerAsync(Guid userId, CancellationToken cancellationToken = default);
}

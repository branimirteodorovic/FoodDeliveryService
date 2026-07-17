using FoodDeliveryService.Common.Domain;

namespace FoodDeliveryService.Modules.Delivery.Application.Abstractions.Provisioning;

/// <summary>
/// Abstraction over the synchronous bus calls to the Users service used during driver onboarding
/// (implemented in Infrastructure with MassTransit request/response, mirroring PermissionService,
/// so the Application layer stays free of messaging concerns).
/// </summary>
public interface IDriverProvisioningService
{
    /// <summary>
    /// Asks Users to create an invited DeliveryDriver account (no password; activation token
    /// emailed by Notifications) and returns the new UserId, or the failure (e.g. duplicate email).
    /// </summary>
    Task<Result<Guid>> ProvisionDriverAsync(
        string email,
        string firstName,
        string lastName,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Compensation for a partial onboarding failure: asks Users to remove the just-provisioned,
    /// still-unactivated driver account so no orphan invited account remains.
    /// </summary>
    Task<Result> DeactivateDriverUserAsync(Guid userId, CancellationToken cancellationToken = default);
}

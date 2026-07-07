namespace FoodDeliveryService.Modules.Users.IntegrationEvents;

/// <summary>
/// Compensating request (MassTransit request/response) sent by the Restaurants module when
/// onboarding fails AFTER the manager account was provisioned: it removes the orphaned invited
/// account so no restaurant-less manager remains (plan §5.1, option a — no saga for this
/// two-step, low-frequency admin flow). Users only honors it for accounts that have never been
/// activated; an already-activated account is refused and surfaces as an Error response.
/// </summary>
public sealed record DeactivateProvisionedUserRequest(Guid UserId);

public sealed record DeactivateProvisionedUserResponse(Guid UserId);

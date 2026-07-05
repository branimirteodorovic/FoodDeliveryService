namespace FoodDeliveryService.Modules.Users.IntegrationEvents;

/// <summary>
/// Synchronous request (MassTransit request/response, same mechanism as GetUserPermissionsRequest)
/// the Restaurants module sends to provision a restaurant-manager account. Users creates an invited
/// account (no password), assigns the RestaurantManager role and replies with the new UserId — or a
/// failure (see <see cref="ProvisionManagerUserResponse"/> / the Error response channel).
/// </summary>
public sealed record ProvisionManagerUserRequest(string Email, string FirstName, string LastName);

public sealed record ProvisionManagerUserResponse(Guid UserId);

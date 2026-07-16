namespace FoodDeliveryService.Modules.Users.IntegrationEvents;

/// <summary>
/// Generalized synchronous request (MassTransit request/response, same mechanism as
/// <see cref="GetUserPermissionsRequest"/>) that any service sends to provision a staff/partner
/// account for a given <paramref name="Role"/>. Users creates an invited account (no password),
/// assigns the role and replies with the new UserId — or an <c>Error</c> response when the role is
/// unknown/non-assignable or the email is a duplicate.
/// <para>
/// Supersedes the role-specific <see cref="ProvisionManagerUserRequest"/>; retargeting Restaurants
/// onto this contract and retiring the manager-specific one is a mechanical follow-up (see
/// DELIVERY_PHASE2_PLAN.md §2.4 / §11).
/// </para>
/// </summary>
public sealed record ProvisionUserRequest(string Email, string FirstName, string LastName, string Role);

public sealed record ProvisionUserResponse(Guid UserId);

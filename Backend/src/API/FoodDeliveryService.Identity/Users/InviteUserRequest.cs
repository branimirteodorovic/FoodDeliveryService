namespace FoodDeliveryService.Identity.Users;

/// <summary>
/// Payload for provisioning an invited account (POST api/users/invite): an admin-created staff
/// account with NO password. The invitee sets their own password later via the activation link.
/// </summary>
public sealed record InviteUserRequest(
    string Email,
    string FirstName,
    string LastName);

namespace FoodDeliveryService.Identity.Users;

/// <summary>
/// Payload for consuming an activation token (POST api/users/set-password): validates the one-time
/// token issued by the invite endpoint, sets the invitee's chosen password and activates the account.
/// </summary>
public sealed record SetPasswordRequest(
    string Email,
    string Token,
    string NewPassword);

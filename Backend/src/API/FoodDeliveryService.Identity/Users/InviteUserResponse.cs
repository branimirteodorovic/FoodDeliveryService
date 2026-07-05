namespace FoodDeliveryService.Identity.Users;

/// <summary>
/// Result of provisioning an invited account: the new identity id plus the one-time activation
/// token (and its expiry) the caller forwards to the invitee. The token is never a password.
/// </summary>
public sealed record InviteUserResponse(
    string Id,
    string ActivationToken,
    DateTime ExpiresOnUtc);

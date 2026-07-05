namespace FoodDeliveryService.Modules.Users.Application.Abstractions.Identity;

public sealed record UserModel(string Email, string Password, string FirstName, string LastName);

/// <summary>Contact details for provisioning an invited account (no password).</summary>
public sealed record InvitedUserModel(string Email, string FirstName, string LastName);

/// <summary>
/// Result of provisioning an invited account: the identity id plus the one-time activation token
/// (and expiry) that must reach the invitee so they can set their password.
/// </summary>
public sealed record InvitationResult(string IdentityId, string ActivationToken, DateTime ExpiresOnUtc);

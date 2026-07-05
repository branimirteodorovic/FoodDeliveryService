namespace FoodDeliveryService.Users.Api.Seed;

/// <summary>
/// Configuration-driven seed for the Users-side administrator record (bound from "AdminSeed").
/// Mirrors the Identity host's AdminSeed section: <see cref="IdentityId"/> MUST equal the id the
/// Identity module used to create the admin credential, because permission resolution joins the
/// module's <c>User</c> to the JWT subject on <c>identity_id</c>. Empty in the committed
/// appsettings.json (no-ops in production); local defaults live in appsettings.Development.json.
/// No password here — credentials live in Identity, not in this module.
/// </summary>
internal sealed class AdminSeedOptions
{
    public const string SectionName = "AdminSeed";

    public string Email { get; init; } = string.Empty;

    public string FirstName { get; init; } = string.Empty;

    public string LastName { get; init; } = string.Empty;

    public string IdentityId { get; init; } = string.Empty;

    public bool IsEnabled =>
        !string.IsNullOrWhiteSpace(Email) &&
        !string.IsNullOrWhiteSpace(IdentityId);
}

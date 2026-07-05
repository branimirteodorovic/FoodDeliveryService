namespace FoodDeliveryService.Identity.Seed;

/// <summary>
/// Configuration-driven initial-administrator seed (bound from the "AdminSeed" section).
/// No one can self-register as an Administrator, so the first admin is created on startup from
/// configuration. Values are empty in the committed appsettings.json so real environments must
/// supply their own secret (Key Vault / env var); appsettings.Development.json holds local defaults.
/// <see cref="IdentityId"/> is the well-known identity id shared with the Users module so that
/// module can seed a matching <c>User</c> row (its <c>IdentityId</c> must equal this value —
/// permission resolution joins on it).
/// </summary>
internal sealed class AdminSeedOptions
{
    public const string SectionName = "AdminSeed";

    public string Email { get; init; } = string.Empty;

    public string Password { get; init; } = string.Empty;

    public string FirstName { get; init; } = string.Empty;

    public string LastName { get; init; } = string.Empty;

    public string IdentityId { get; init; } = string.Empty;

    public bool IsEnabled =>
        !string.IsNullOrWhiteSpace(Email) &&
        !string.IsNullOrWhiteSpace(Password) &&
        !string.IsNullOrWhiteSpace(IdentityId);
}

using Duende.IdentityServer;
using Duende.IdentityServer.Models;

namespace FoodDeliveryService.Identity;

/// <summary>
/// In-memory IdentityServer configuration (identity resources, API scopes,
/// API resources and clients) for the FoodDeliveryService platform.
/// </summary>
internal static class Config
{
    // Scope used by the user-facing APIs (Orders, Restaurants, Notifications, Users).
    private const string ApiScope = "fooddeliveryservice.api";

    // Scope used by the confidential client to call the user-registration endpoint.
    public const string UsersRegisterScope = "users:register";

    public const string UsersRegisterPolicy = "UsersRegister";

    public static IEnumerable<IdentityResource> IdentityResources =>
    [
        new IdentityResources.OpenId(),
        new IdentityResources.Profile(),
        new IdentityResources.Email()
    ];

    public static IEnumerable<ApiScope> ApiScopes =>
    [
        new ApiScope(ApiScope, "FoodDeliveryService API"),
        new ApiScope(UsersRegisterScope, "Register users in the identity provider")
    ];

    public static IEnumerable<ApiResource> ApiResources =>
    [
        new ApiResource(ApiScope, "FoodDeliveryService API")
        {
            Scopes = { ApiScope }
        }
    ];

    public static IEnumerable<Client> Clients(IConfiguration configuration)
    {
        string confidentialClientId =
            configuration["Clients:Confidential:ClientId"] ?? "fooddeliveryservice-confidential-client";
        // Feature 3.7 Milestone E: NO fallback. This used to default to the value committed in
        // appsettings.Development.json, which quietly defeated the whole secrets model — Milestone B
        // made appsettings.json ship the key blank so a real environment must supply it, and this
        // line handed that environment the committed secret instead. A blank value now produces a
        // client with no secret at all, which fails closed (nothing can authenticate as it) rather
        // than open. Outside Development the host does not get this far: AddRequiredConfiguration in
        // Program.cs fails the boot naming the key. docs/security.md §6.2.
        string confidentialClientSecret = configuration["Clients:Confidential:ClientSecret"] ?? string.Empty;
        string publicClientId =
            configuration["Clients:Public:ClientId"] ?? "fooddeliveryservice-public-client";

        return
        [
            // Machine-to-machine client used by the Users module to register new users.
            new Client
            {
                ClientId = confidentialClientId,
                ClientName = "FoodDeliveryService Confidential Client",
                AllowedGrantTypes = GrantTypes.ClientCredentials,
                ClientSecrets = string.IsNullOrWhiteSpace(confidentialClientSecret)
                    ? []
                    : [new Secret(confidentialClientSecret.Sha256())],
                AllowedScopes = { UsersRegisterScope },
                // Provisioning is a handful of calls per registration and the token is fetched fresh
                // for each one (DuendeAuthDelegatingHandler does not cache), so a long-lived
                // machine-to-machine token buys nothing and widens the window if one leaks.
                AccessTokenLifetime = (int)TimeSpan.FromMinutes(5).TotalSeconds
            },

            // Public client used by end users to obtain access tokens for the APIs.
            // Resource-owner-password only: a public (secret-less) client cannot use the
            // client_credentials grant, and Duende rejects that combination outright
            // ("RequireClientSecret is false, but client is using client credentials grant type").
            new Client
            {
                ClientId = publicClientId,
                ClientName = "FoodDeliveryService Public Client",
                AllowedGrantTypes = GrantTypes.ResourceOwnerPassword,
                RequireClientSecret = false,
                AllowOfflineAccess = true,

                // Token lifetimes, Feature 3.7 Milestone E (docs/security.md §6.3). Duende's default
                // access-token lifetime is an hour; nothing in this platform can revoke an issued
                // JWT — permissions are re-resolved per request but the token itself is validated by
                // signature alone — so an hour is how long a stolen token stays useful. Fifteen
                // minutes plus a rotating refresh token is the standard trade: the SPA refreshes
                // four times an hour instead of once, and every refresh re-checks the account still
                // exists and is not locked out.
                AccessTokenLifetime = (int)TimeSpan.FromMinutes(15).TotalSeconds,
                // One-time-only refresh tokens: using one twice invalidates the whole chain, which
                // is what makes a leaked refresh token detectable rather than silently useful. This
                // requires the persisted-grant store the operational store registration adds — with
                // the in-memory store the rotation state died with the process.
                RefreshTokenUsage = TokenUsage.OneTimeOnly,
                RefreshTokenExpiration = TokenExpiration.Sliding,
                // Eight idle hours ends a session; seven days ends it regardless of activity.
                SlidingRefreshTokenLifetime = (int)TimeSpan.FromHours(8).TotalSeconds,
                AbsoluteRefreshTokenLifetime = (int)TimeSpan.FromDays(7).TotalSeconds,
                // A refresh must pick up a role or permission change, not re-stamp the claims minted
                // at login — otherwise a revoked account keeps working until the absolute lifetime.
                UpdateAccessTokenClaimsOnRefresh = true,

                AllowedScopes =
                {
                    IdentityServerConstants.StandardScopes.OpenId,
                    IdentityServerConstants.StandardScopes.Profile,
                    IdentityServerConstants.StandardScopes.Email,
                    ApiScope
                }
            }
        ];
    }
}

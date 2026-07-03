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
    private const string UsersRegisterScope = "users:register";

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
        string confidentialClientSecret =
            configuration["Clients:Confidential:ClientSecret"] ?? "PzotcrvZRF9BHCKcUxdKfHWlIPECG49k";
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
                ClientSecrets = { new Secret(confidentialClientSecret.Sha256()) },
                AllowedScopes = { UsersRegisterScope }
            },

            // Public client used by end users to obtain access tokens for the APIs.
            new Client
            {
                ClientId = publicClientId,
                ClientName = "FoodDeliveryService Public Client",
                AllowedGrantTypes = GrantTypes.ResourceOwnerPasswordAndClientCredentials,
                RequireClientSecret = false,
                AllowOfflineAccess = true,
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

using System.Net.Http.Json;

namespace FoodDeliveryService.Modules.Users.Infrastructure.Identity;

/// <summary>
/// Typed HttpClient for Duende IdentityServer's user-provisioning local API (POST api/users).
/// Called during registration to create the credential record in the identity database; the
/// returned id links the module-side User to its Duende identity. Authentication is handled by
/// DuendeAuthDelegatingHandler. This Users → Identity call is the only exception to the
/// "no HTTP between services" rule.
/// </summary>
internal sealed class DuendeIdentityClient(HttpClient httpClient)
{
    internal async Task<string> RegisterUserAsync(
        RegisterUserRequest user,
        CancellationToken cancellationToken = default)
    {
        HttpResponseMessage httpResponseMessage = await httpClient.PostAsJsonAsync(
            "users",
            user,
            cancellationToken);

        httpResponseMessage.EnsureSuccessStatusCode();

        RegisterUserResponse? response = await httpResponseMessage.Content
            .ReadFromJsonAsync<RegisterUserResponse>(cancellationToken);

        if (response is null || string.IsNullOrWhiteSpace(response.Id))
        {
            throw new InvalidOperationException(
                "The identity provider did not return a user identifier.");
        }

        return response.Id;
    }
}

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

    // POST api/users/invite — provisions an invited account (no password) and returns the identity
    // id + one-time activation token.
    internal async Task<InviteUserResponse> InviteUserAsync(
        InviteUserRequest user,
        CancellationToken cancellationToken = default)
    {
        HttpResponseMessage httpResponseMessage = await httpClient.PostAsJsonAsync(
            "users/invite",
            user,
            cancellationToken);

        httpResponseMessage.EnsureSuccessStatusCode();

        InviteUserResponse? response = await httpResponseMessage.Content
            .ReadFromJsonAsync<InviteUserResponse>(cancellationToken);

        if (response is null ||
            string.IsNullOrWhiteSpace(response.Id) ||
            string.IsNullOrWhiteSpace(response.ActivationToken))
        {
            throw new InvalidOperationException(
                "The identity provider did not return an invitation result.");
        }

        return response;
    }

    // DELETE api/users/{id} — removes a never-activated invited account (onboarding
    // compensation). Identity answers 409 for an already-activated account.
    internal async Task DeleteInvitedUserAsync(
        string identityId,
        CancellationToken cancellationToken = default)
    {
        HttpResponseMessage httpResponseMessage = await httpClient.DeleteAsync(
            $"users/{Uri.EscapeDataString(identityId)}",
            cancellationToken);

        // Already gone == compensation goal reached; keep the call idempotent.
        if (httpResponseMessage.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return;
        }

        httpResponseMessage.EnsureSuccessStatusCode();
    }

    // POST api/users/set-password — consumes the activation token and sets the invitee's password.
    internal async Task SetPasswordAsync(
        SetPasswordRequest request,
        CancellationToken cancellationToken = default)
    {
        HttpResponseMessage httpResponseMessage = await httpClient.PostAsJsonAsync(
            "users/set-password",
            request,
            cancellationToken);

        httpResponseMessage.EnsureSuccessStatusCode();
    }
}

using System.Net.Http.Json;

namespace FoodDeliveryService.Modules.Users.Infrastructure.Identity;

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

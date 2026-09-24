using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Bogus;
using FoodDeliveryService.Modules.Users.Presentation.Users;
using Microsoft.Extensions.DependencyInjection;

namespace FoodDeliveryService.Modules.Users.IntegrationTests.Abstractions;

[Collection(nameof(IntegrationTestCollection))]
public class BaseIntegrationTest : IDisposable
{
    private const string TokenEndpoint = "http://localhost:18080/connect/token";
    private const string PublicClientId = "fooddeliveryservice-public-client";

    protected static readonly Faker Faker = new();
    private readonly IServiceScope _scope;
    protected readonly HttpClient HttpClient;

    public BaseIntegrationTest(IntegrationTestWebAppFactory factory)
    {
        Factory = factory;
        _scope = factory.Services.CreateScope();
        HttpClient = factory.CreateClient();
    }

    protected IntegrationTestWebAppFactory Factory { get; }

    /// <summary>
    /// A globally-unique email. Identity's ASP.NET Identity store is real and persistent (not a
    /// testcontainer), so a fixed or Faker-random address could collide across repeated local runs and
    /// fail registration; the embedded <see cref="Guid"/> guarantees a fresh account every time.
    /// </summary>
    protected static string UniqueEmail() => $"users-tests+{Guid.NewGuid():N}@fooddeliveryservice.com";

    /// <summary>
    /// A password that satisfies ASP.NET Identity's full default strength policy (length, digit,
    /// lower, upper and non-alphanumeric). Identity relaxes those rules only when it runs in the
    /// Development environment; the instance these tests talk to on :18080 does not, so a password
    /// has to clear the strict policy. <c>Faker.Internet.Password</c> cannot: it draws from word
    /// characters only, so it never emits a non-alphanumeric one and randomly omits an uppercase
    /// one — registration then failed intermittently with a 500. Fixed literal, matching what every
    /// other module's fixture uses.
    /// </summary>
    protected const string StrongPassword = "Users-Tests-P@ssw0rd1";

    /// <summary>
    /// Registers a customer through the real <c>users/register</c> endpoint and returns the
    /// <b>module-side</b> user id the command answers with — the <c>users.id</c> value, which is
    /// what <c>users/me</c> must report back and what every other service keys a customer by.
    /// </summary>
    protected async Task<Guid> RegisterCustomerAsync(string email)
    {
        var request = new RegisterUser.Request
        {
            Email = email,
            Password = StrongPassword,
            FirstName = Faker.Name.FirstName(),
            LastName = Faker.Name.LastName(),
        };

        HttpResponseMessage response = await HttpClient.PostAsJsonAsync(
            "users/register",
            request,
            TestContext.Current.CancellationToken);

        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<Guid>(TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// A password-grant access token from the real Duende instance on <c>:18080</c>, and a client
    /// carrying it. Nothing is faked here: the token these tests send is the token a browser would,
    /// so <c>CustomClaimsTransformation</c> does its real lookup and the claims under test are the
    /// ones production mints.
    /// </summary>
    protected async Task<HttpClient> CreateClientForUserAsync(string email, string password)
    {
        string accessToken = await GetAccessTokenAsync(email, password);

        HttpClient client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        return client;
    }

    protected static async Task<string> GetAccessTokenAsync(string email, string password)
    {
        using var client = new HttpClient();

        using var content = new FormUrlEncodedContent(
        [
            new KeyValuePair<string, string>("client_id", PublicClientId),
            new KeyValuePair<string, string>("scope", "openid profile email fooddeliveryservice.api"),
            new KeyValuePair<string, string>("grant_type", "password"),
            new KeyValuePair<string, string>("username", email),
            new KeyValuePair<string, string>("password", password)
        ]);

        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(TokenEndpoint))
        {
            Content = content
        };

        using HttpResponseMessage response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        response.EnsureSuccessStatusCode();

        AuthToken? token = await response.Content.ReadFromJsonAsync<AuthToken>(
            TestContext.Current.CancellationToken);

        return token!.AccessToken;
    }

    public void Dispose()
    {
        _scope.Dispose();

        GC.SuppressFinalize(this);
    }

    private sealed class AuthToken
    {
        [JsonPropertyName("access_token")]
        public string AccessToken { get; init; } = string.Empty;
    }
}

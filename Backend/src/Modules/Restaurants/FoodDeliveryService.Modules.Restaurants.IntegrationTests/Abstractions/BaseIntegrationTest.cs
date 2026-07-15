using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Bogus;
using FoodDeliveryService.Modules.Restaurants.Presentation.Restaurants;
using Microsoft.Extensions.DependencyInjection;

namespace FoodDeliveryService.Modules.Restaurants.IntegrationTests.Abstractions;

[Collection(nameof(IntegrationTestCollection))]
public class BaseIntegrationTest : IDisposable
{
    private const string TokenEndpoint = "http://localhost:18080/connect/token";
    private const string PublicClientId = "fooddeliveryservice-public-client";

    // All test classes share the same IntegrationTestWebAppFactory (one per collection), so the
    // token for the single seeded test user is fetched once and reused by every test.
    private static readonly SemaphoreSlim TokenLock = new(1, 1);
    private static string? _cachedAccessToken;

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

    public void Dispose()
    {
        _scope.Dispose();
    }

    /// <summary>
    /// Attaches a Bearer token for the collection's single seeded test user (real Identity
    /// credential + real Users-module Administrator row) to <see cref="HttpClient"/>, so tests can
    /// drive real, authorized HTTP endpoints instead of calling <see cref="Sender"/> directly.
    /// </summary>
    protected async Task<HttpClient> GetAuthenticatedHttpClientAsync()
    {
        string accessToken = await GetOrCreateAccessTokenAsync();

        HttpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        return HttpClient;
    }

    /// <summary>
    /// Onboards a restaurant through the real endpoint and returns its id — the starting point for
    /// every test that needs an existing restaurant to act on. Each call provisions a fresh manager
    /// (unique email), so restaurants never collide across tests sharing the collection's database.
    /// </summary>
    protected static async Task<Guid> OnboardRestaurantAsync(HttpClient client, decimal commissionRate = 0.30m)
    {
        var request = new OnboardRestaurant.Request
        {
            Name = Faker.Company.CompanyName(),
            TaxIdentification = Faker.Random.Replace("##########"),
            CuisineType = "Italian",
            Email = Faker.Internet.Email(),
            PhoneNumber = Faker.Phone.PhoneNumber(),
            Street = Faker.Address.StreetAddress(),
            City = Faker.Address.City(),
            PostalCode = Faker.Address.ZipCode(),
            Country = Faker.Address.Country(),
            Latitude = Faker.Address.Latitude(),
            Longitude = Faker.Address.Longitude(),
            CommissionRate = commissionRate,
            ManagerEmail = Faker.Internet.Email(),
            ManagerFirstName = Faker.Name.FirstName(),
            ManagerLastName = Faker.Name.LastName(),
        };

        HttpResponseMessage response = await client.PostAsJsonAsync(
            "restaurants",
            request,
            TestContext.Current.CancellationToken);

        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<Guid>(TestContext.Current.CancellationToken);
    }

    private async Task<string> GetOrCreateAccessTokenAsync()
    {
        await TokenLock.WaitAsync();

        try
        {
            _cachedAccessToken ??= await GetAccessTokenAsync(Factory.TestUserEmail, Factory.TestUserPassword);

            return _cachedAccessToken;
        }
        finally
        {
            TokenLock.Release();
        }
    }

    protected static async Task<string> GetAccessTokenAsync(string email, string password)
    {
        using var client = new HttpClient();

        var authRequestParameters = new KeyValuePair<string, string>[]
            {
                new("client_id", PublicClientId),
                new("scope", "openid profile email fooddeliveryservice.api"),
                new("grant_type", "password"),
                new("username", email),
                new("password", password)
            };

        using var authRequestContent = new FormUrlEncodedContent(authRequestParameters);

        using var authRequest = new HttpRequestMessage(HttpMethod.Post, new Uri(TokenEndpoint));
        authRequest.Content = authRequestContent;

        using HttpResponseMessage authorizationResponse = await client.SendAsync(authRequest);

        authorizationResponse.EnsureSuccessStatusCode();

        AuthToken authToken = await authorizationResponse.Content.ReadFromJsonAsync<AuthToken>();

        return authToken!.AccessToken;
    }

    internal sealed class AuthToken
    {
        [JsonPropertyName("access_token")]
        public string AccessToken { get; init; }
    }
}

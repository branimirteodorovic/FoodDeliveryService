using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Bogus;
using Microsoft.Extensions.DependencyInjection;

namespace FoodDeliveryService.Modules.Payments.IntegrationTests.Abstractions;

[Collection(nameof(IntegrationTestCollection))]
public class BaseIntegrationTest : IDisposable
{
    private const string TokenEndpoint = "http://localhost:18080/connect/token";
    private const string PublicClientId = "fooddeliveryservice-public-client";

    // Every test class shares one IntegrationTestWebAppFactory (one per collection), so the tokens
    // for the seeded users are fetched once and reused.
    private static readonly SemaphoreSlim TokenLock = new(1, 1);
    private static string? _cachedCustomerAccessToken;
    private static string? _cachedOtherCustomerAccessToken;
    private static string? _cachedManagerAccessToken;

    protected static readonly Faker Faker = new();
    private readonly IServiceScope _scope;

    public BaseIntegrationTest(IntegrationTestWebAppFactory factory)
    {
        Factory = factory;
        _scope = factory.Services.CreateScope();
    }

    protected IntegrationTestWebAppFactory Factory { get; }

    public void Dispose()
    {
        _scope.Dispose();
        GC.SuppressFinalize(this);
    }

    /// <summary>A client for the seeded Customer — holds payment-methods:manage and payments:read.</summary>
    protected async Task<HttpClient> CreateCustomerClientAsync()
    {
        await TokenLock.WaitAsync();

        try
        {
            _cachedCustomerAccessToken ??=
                await GetAccessTokenAsync(Factory.CustomerUserEmail, Factory.TestUserPassword);
        }
        finally
        {
            TokenLock.Release();
        }

        return CreateClientWithToken(_cachedCustomerAccessToken);
    }

    /// <summary>A client for a different Customer — proves the ownership scoping is real.</summary>
    protected async Task<HttpClient> CreateOtherCustomerClientAsync()
    {
        await TokenLock.WaitAsync();

        try
        {
            _cachedOtherCustomerAccessToken ??=
                await GetAccessTokenAsync(Factory.OtherCustomerUserEmail, Factory.TestUserPassword);
        }
        finally
        {
            TokenLock.Release();
        }

        return CreateClientWithToken(_cachedOtherCustomerAccessToken);
    }

    /// <summary>
    /// A client for the seeded RestaurantManager — authenticated, and holding none of the three
    /// payment permission codes. The 403 case.
    /// </summary>
    protected async Task<HttpClient> CreateManagerClientAsync()
    {
        await TokenLock.WaitAsync();

        try
        {
            _cachedManagerAccessToken ??=
                await GetAccessTokenAsync(Factory.ManagerUserEmail, Factory.TestUserPassword);
        }
        finally
        {
            TokenLock.Release();
        }

        return CreateClientWithToken(_cachedManagerAccessToken);
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

        using var authRequest = new HttpRequestMessage(HttpMethod.Post, new Uri(TokenEndpoint))
        {
            Content = authRequestContent
        };

        using HttpResponseMessage authorizationResponse = await client.SendAsync(authRequest);

        authorizationResponse.EnsureSuccessStatusCode();

        AuthToken? authToken = await authorizationResponse.Content.ReadFromJsonAsync<AuthToken>();

        return authToken!.AccessToken;
    }

    private HttpClient CreateClientWithToken(string accessToken)
    {
        HttpClient client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        return client;
    }

    internal sealed class AuthToken
    {
        [JsonPropertyName("access_token")]
        public string AccessToken { get; init; } = string.Empty;
    }
}

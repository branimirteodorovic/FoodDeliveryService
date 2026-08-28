using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Bogus;
using FoodDeliveryService.Modules.Support.Presentation.Tickets;
using Microsoft.Extensions.DependencyInjection;

namespace FoodDeliveryService.Modules.Support.IntegrationTests.Abstractions;

[Collection(nameof(IntegrationTestCollection))]
public class BaseIntegrationTest : IDisposable
{
    private const string TokenEndpoint = "http://localhost:18080/connect/token";
    private const string PublicClientId = "fooddeliveryservice-public-client";

    // All test classes share the same IntegrationTestWebAppFactory (one per collection), so the
    // tokens for the seeded users are fetched once and reused by every test.
    private static readonly SemaphoreSlim TokenLock = new(1, 1);
    private static string? _cachedAgentAccessToken;
    private static string? _cachedCustomerAccessToken;
    private static string? _cachedOtherCustomerAccessToken;
    private static string? _cachedOtherAgentAccessToken;
    private static string? _cachedAdminAccessToken;

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

    /// <summary>
    /// A client for the seeded SupportAgent — holds support-tickets:read/manage/assign, and NOT
    /// support-tickets:open.
    /// </summary>
    protected async Task<HttpClient> CreateAgentClientAsync()
    {
        await TokenLock.WaitAsync();

        try
        {
            _cachedAgentAccessToken ??= await GetAccessTokenAsync(Factory.AgentUserEmail, Factory.TestUserPassword);
        }
        finally
        {
            TokenLock.Release();
        }

        return CreateClientWithToken(_cachedAgentAccessToken);
    }

    /// <summary>A client for the seeded Customer — support-tickets:open and :read only.</summary>
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

    /// <summary>A client for the second seeded SupportAgent — the "somebody else" of assignment.</summary>
    protected async Task<HttpClient> CreateOtherAgentClientAsync()
    {
        await TokenLock.WaitAsync();

        try
        {
            _cachedOtherAgentAccessToken ??=
                await GetAccessTokenAsync(Factory.OtherAgentUserEmail, Factory.TestUserPassword);
        }
        finally
        {
            TokenLock.Release();
        }

        return CreateClientWithToken(_cachedOtherAgentAccessToken);
    }

    /// <summary>
    /// A client for the seeded Administrator — the only caller holding support-tickets:administer,
    /// and therefore the only one who can assign a ticket to an agent other than themselves.
    /// </summary>
    protected async Task<HttpClient> CreateAdminClientAsync()
    {
        await TokenLock.WaitAsync();

        try
        {
            _cachedAdminAccessToken ??= await GetAccessTokenAsync(Factory.AdminUserEmail, Factory.TestUserPassword);
        }
        finally
        {
            TokenLock.Release();
        }

        return CreateClientWithToken(_cachedAdminAccessToken);
    }

    /// <summary>Opens a ticket through the real endpoint and returns its id.</summary>
    protected static async Task<Guid> OpenTicketAsync(
        HttpClient client,
        string? subject = null,
        string category = "Other",
        Guid? orderId = null)
    {
        var request = new OpenTicket.Request
        {
            OrderId = orderId,
            Subject = subject ?? Faker.Lorem.Sentence(),
            Category = category
        };

        HttpResponseMessage response = await client.PostAsJsonAsync(
            "support/tickets",
            request,
            TestContext.Current.CancellationToken);

        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<Guid>(TestContext.Current.CancellationToken);
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

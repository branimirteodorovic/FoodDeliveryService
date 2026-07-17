using System.Data.Common;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Bogus;
using Dapper;
using FoodDeliveryService.Common.Application.Data;
using FoodDeliveryService.Common.Domain;
using FoodDeliveryService.Modules.Delivery.Presentation.Drivers;
using Microsoft.Extensions.DependencyInjection;

namespace FoodDeliveryService.Modules.Delivery.IntegrationTests.Abstractions;

[Collection(nameof(IntegrationTestCollection))]
public class BaseIntegrationTest : IDisposable
{
    private const string TokenEndpoint = "http://localhost:18080/connect/token";
    private const string PublicClientId = "fooddeliveryservice-public-client";

    // All test classes share the same IntegrationTestWebAppFactory (one per collection), so the
    // tokens for the seeded admin/customer users are fetched once and reused by every test.
    private static readonly SemaphoreSlim TokenLock = new(1, 1);
    private static string? _cachedAdminAccessToken;
    private static string? _cachedCustomerAccessToken;

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
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// A fresh client carrying a Bearer token for the seeded Administrator (users:provision +
    /// deliveries:administer) — the caller for onboarding and admin-bypass reads.
    /// </summary>
    protected async Task<HttpClient> CreateAdminClientAsync()
    {
        await TokenLock.WaitAsync();

        try
        {
            _cachedAdminAccessToken ??=
                await GetAccessTokenAsync(Factory.AdminUserEmail, Factory.TestUserPassword);
        }
        finally
        {
            TokenLock.Release();
        }

        return CreateClientWithToken(_cachedAdminAccessToken);
    }

    /// <summary>
    /// A fresh client carrying a Bearer token for the seeded Customer — no driver/provisioning
    /// permissions, used to prove the authorization failures.
    /// </summary>
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

    /// <summary>A fresh client authenticated as an arbitrary (already activated) user.</summary>
    protected async Task<HttpClient> CreateClientForUserAsync(string email, string password)
    {
        string accessToken = await GetAccessTokenAsync(email, password);

        return CreateClientWithToken(accessToken);
    }

    /// <summary>
    /// Onboards a driver through the real endpoint (admin caller) and returns the driver id and
    /// the unique email the invited account was provisioned with.
    /// </summary>
    protected static async Task<(Guid DriverId, string Email)> OnboardDriverAsync(
        HttpClient adminClient,
        string vehicleType = "Bicycle")
    {
        string email = $"driver+{Guid.NewGuid():N}@fooddeliveryservice.com";

        var request = new OnboardDriver.Request
        {
            Email = email,
            FirstName = Faker.Name.FirstName(),
            LastName = Faker.Name.LastName(),
            VehicleType = vehicleType,
        };

        HttpResponseMessage response = await adminClient.PostAsJsonAsync(
            "delivery/drivers",
            request,
            TestContext.Current.CancellationToken);

        response.EnsureSuccessStatusCode();

        Guid driverId = await response.Content.ReadFromJsonAsync<Guid>(TestContext.Current.CancellationToken);

        return (driverId, email);
    }

    /// <summary>
    /// Activates an invited driver account the way the real invitee would: pulls the one-time
    /// activation token from the Users test host's outbox (the UserInvitedDomainEvent raised by
    /// provisioning — the same payload Notifications would email) and posts it to the real
    /// users/accept-invitation endpoint with the new password.
    /// </summary>
    protected async Task ActivateDriverAsync(string email, string newPassword)
    {
        string activationToken = await GetActivationTokenAsync(email);

        using HttpClient usersClient = Factory.UsersApi.CreateClient();

        HttpResponseMessage response = await usersClient.PostAsJsonAsync(
            "users/accept-invitation",
            new { Email = email, Token = activationToken, NewPassword = newPassword },
            TestContext.Current.CancellationToken);

        response.EnsureSuccessStatusCode();
    }

    protected static async Task<string> GetAccessTokenAsync(string email, string password)
    {
        using HttpResponseMessage authorizationResponse = await RequestTokenAsync(email, password);

        authorizationResponse.EnsureSuccessStatusCode();

        AuthToken? authToken = await authorizationResponse.Content.ReadFromJsonAsync<AuthToken>();

        return authToken!.AccessToken;
    }

    /// <summary>
    /// Raw password-grant token request — lets tests assert that an invited (not yet activated)
    /// account cannot log in, without throwing.
    /// </summary>
    protected static async Task<HttpResponseMessage> RequestTokenAsync(string email, string password)
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

        return await client.SendAsync(authRequest);
    }

    private HttpClient CreateClientWithToken(string accessToken)
    {
        HttpClient client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        return client;
    }

    /// <summary>
    /// Scrapes the invited account's one-time activation token from the Users test host's
    /// outbox_messages table (UserInvitedDomainEvent, written transactionally by provisioning).
    /// The row persists after processing, but poll briefly anyway to absorb commit timing.
    /// </summary>
    private async Task<string> GetActivationTokenAsync(string email)
    {
        await using AsyncServiceScope scope = Factory.UsersApi.Services.CreateAsyncScope();
        var dbConnectionFactory = scope.ServiceProvider.GetRequiredService<IDbConnectionFactory>();

        Result<string> token = await Poller.WaitAsync(TimeSpan.FromSeconds(15), async () =>
        {
            await using DbConnection connection = await dbConnectionFactory.OpenConnectionAsync();

            const string sql =
                """
                SELECT content
                FROM outbox_messages
                WHERE type = 'UserInvitedDomainEvent'
                ORDER BY occurred_on_utc DESC
                """;

            IEnumerable<string> contents = await connection.QueryAsync<string>(sql);

            foreach (string content in contents)
            {
                using var document = JsonDocument.Parse(content);

                if (document.RootElement.GetProperty("Email").GetString() == email)
                {
                    return Result.Success(document.RootElement.GetProperty("ActivationToken").GetString()!);
                }
            }

            return Result.Failure<string>(Error.NotFound(
                "ActivationToken.NotFound",
                $"No UserInvitedDomainEvent for {email} yet"));
        });

        if (token.IsFailure)
        {
            throw new InvalidOperationException($"Activation token for {email} never appeared in the Users outbox.");
        }

        return token.Value;
    }

    internal sealed class AuthToken
    {
        [JsonPropertyName("access_token")]
        public string AccessToken { get; init; } = string.Empty;
    }
}

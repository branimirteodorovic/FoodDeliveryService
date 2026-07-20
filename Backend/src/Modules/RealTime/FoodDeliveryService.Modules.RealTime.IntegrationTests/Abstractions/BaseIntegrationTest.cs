using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.Client;

namespace FoodDeliveryService.Modules.RealTime.IntegrationTests.Abstractions;

[Collection(nameof(IntegrationTestCollection))]
public class BaseIntegrationTest
{
    private const string TokenEndpoint = "http://localhost:18080/connect/token";
    private const string PublicClientId = "fooddeliveryservice-public-client";
    private const string HubPath = "hubs/tracking";

    // All test classes share the same IntegrationTestWebAppFactory (one per collection), so the
    // token for the single seeded test user is fetched once and reused by every test.
    private static readonly SemaphoreSlim TokenLock = new(1, 1);
    private static string? _cachedAccessToken;

    public BaseIntegrationTest(IntegrationTestWebAppFactory factory)
    {
        Factory = factory;
    }

    protected IntegrationTestWebAppFactory Factory { get; }

    /// <summary>
    /// Builds a real SignalR client bound to the in-process host. It uses the WebSocket transport and
    /// carries the JWT as the <c>access_token</c> query parameter — exactly the browser handshake the
    /// JwtBearer <c>OnMessageReceived</c> hook is there to support — routed through the TestServer's
    /// own WebSocket client (there is no real socket listener). Pass <paramref name="accessToken"/>
    /// null to simulate an anonymous handshake.
    /// </summary>
    protected HubConnection BuildHubConnection(string? accessToken, bool withAutomaticReconnect = false)
    {
        IHubConnectionBuilder builder = new HubConnectionBuilder()
            .WithUrl(new Uri(Factory.Server.BaseAddress, HubPath), options =>
            {
                options.Transports = HttpTransportType.WebSockets;
                options.SkipNegotiation = true;
                options.WebSocketFactory = async (context, cancellationToken) =>
                {
                    var webSocketClient = Factory.Server.CreateWebSocketClient();

                    var uriBuilder = new UriBuilder(context.Uri);
                    if (accessToken is not null)
                    {
                        uriBuilder.Query = $"access_token={Uri.EscapeDataString(accessToken)}";
                    }

                    return await webSocketClient.ConnectAsync(uriBuilder.Uri, cancellationToken);
                };
            });

        if (withAutomaticReconnect)
        {
            builder = builder.WithAutomaticReconnect();
        }

        return builder.Build();
    }

    protected async Task<string> GetAccessTokenAsync() => await GetOrCreateAccessTokenAsync();

    private async Task<string> GetOrCreateAccessTokenAsync()
    {
        await TokenLock.WaitAsync();

        try
        {
            _cachedAccessToken ??= await RequestAccessTokenAsync(Factory.TestUserEmail, Factory.TestUserPassword);

            return _cachedAccessToken;
        }
        finally
        {
            TokenLock.Release();
        }
    }

    private static async Task<string> RequestAccessTokenAsync(string email, string password)
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

        AuthToken authToken = (await authorizationResponse.Content.ReadFromJsonAsync<AuthToken>())!;

        return authToken.AccessToken;
    }

    private sealed class AuthToken
    {
        [JsonPropertyName("access_token")]
        public string AccessToken { get; init; } = string.Empty;
    }
}

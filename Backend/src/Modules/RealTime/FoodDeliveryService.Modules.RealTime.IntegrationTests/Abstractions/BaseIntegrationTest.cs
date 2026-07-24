using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using FoodDeliveryService.Common.Application.Caching;
using FoodDeliveryService.Common.Application.EventBus;
using FoodDeliveryService.Common.Domain;
using FoodDeliveryService.Modules.RealTime.Application.RealTime;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using StackExchange.Redis;

namespace FoodDeliveryService.Modules.RealTime.IntegrationTests.Abstractions;

[Collection(nameof(IntegrationTestCollection))]
public class BaseIntegrationTest
{
    private const string TokenEndpoint = "http://localhost:18080/connect/token";
    private const string PublicClientId = "fooddeliveryservice-public-client";
    private const string HubPath = "hubs/tracking";

    // All test classes share the same IntegrationTestWebAppFactory (one per collection), so each
    // seeded test user's token is fetched once (keyed by email) and reused by every test.
    private static readonly SemaphoreSlim TokenLock = new(1, 1);
    private static readonly Dictionary<string, string> CachedAccessTokensByEmail = [];

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

    protected Task<string> GetAccessTokenAsync() =>
        GetOrCreateAccessTokenAsync(Factory.TestUserEmail, Factory.TestUserPassword);

    /// <summary>Milestone D: the seeded RestaurantManager test user's token.</summary>
    protected Task<string> GetRestaurantManagerAccessTokenAsync() =>
        GetOrCreateAccessTokenAsync(Factory.RestaurantManagerEmail, Factory.RestaurantManagerPassword);

    /// <summary>Milestone D: the seeded SupportAgent test user's token.</summary>
    protected Task<string> GetSupportAgentAccessTokenAsync() =>
        GetOrCreateAccessTokenAsync(Factory.SupportAgentEmail, Factory.SupportAgentPassword);

    /// <summary>
    /// Publishes an integration event onto the real RabbitMQ broker through the host's own
    /// <see cref="IEventBus"/> — exactly as Orders would in production — so the RealTime direct
    /// consumers pick it up on their own queues and fan it out to the hub.
    /// </summary>
    protected Task PublishAsync<T>(T integrationEvent, CancellationToken cancellationToken = default)
        where T : IIntegrationEvent =>
        Factory.Services.GetRequiredService<IEventBus>().PublishAsync(integrationEvent, cancellationToken);

    /// <summary>
    /// Reads the ephemeral routing row a status consumer writes at <c>rt:order:{orderId}</c>. The key
    /// format mirrors <c>OrderRoutingMap</c>; reading it back proves the map was warmed.
    /// </summary>
    protected Task<OrderRoutingEntry?> GetOrderRoutingAsync(Guid orderId, CancellationToken cancellationToken = default) =>
        Factory.Services.GetRequiredService<ICacheService>()
            .GetAsync<OrderRoutingEntry>($"rt:order:{orderId}", cancellationToken);

    /// <summary>
    /// Reads the Milestone C driver→order/customer binding a Delivery-event consumer writes at
    /// <c>rt:driver:{driverId}</c> (see <c>DriverBindingStore</c>) — proves a binding was set/cleared.
    /// </summary>
    protected Task<DriverBinding?> GetDriverBindingAsync(Guid driverId, CancellationToken cancellationToken = default) =>
        Factory.Services.GetRequiredService<ICacheService>()
            .GetAsync<DriverBinding>($"rt:driver:{driverId}", cancellationToken);

    /// <summary>
    /// Milestone D: polls the RestaurantManager replica (built by ProcessInboxJob off a published
    /// RestaurantRegisteredIntegrationEvent, on its own interval — not synchronous with the publish)
    /// until the row for <paramref name="managerUserId"/> lands, or the timeout elapses.
    /// </summary>
    protected async Task<Guid?> WaitForRestaurantManagerReplicaAsync(
        Guid managerUserId,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        Result<Guid> result = await Poller.WaitAsync<Guid>(timeout, async () =>
        {
            // IRestaurantManagerStore is scoped (it depends on the scoped RealTimeDbContext for the
            // write path), so it must be resolved from a scope — not the root provider.
            await using AsyncServiceScope scope = Factory.Services.CreateAsyncScope();
            IRestaurantManagerStore store = scope.ServiceProvider.GetRequiredService<IRestaurantManagerStore>();

            Guid? restaurantId = await store.GetRestaurantIdAsync(managerUserId, cancellationToken);

            return restaurantId is null
                ? Result.Failure<Guid>(Error.Failure("RestaurantManager.NotReplicatedYet", "No replica row yet."))
                : restaurantId.Value;
        });

        return result.IsSuccess ? result.Value : null;
    }

    /// <summary>
    /// Publishes a driver-position message on the same Redis channel Delivery's
    /// <c>RedisDriverLocationStore</c> publishes to in production — the real path here is a
    /// PUBLISH, not the bus (plan §4.1), so this bypasses RabbitMQ entirely on purpose.
    /// </summary>
    protected Task PublishDriverLocationAsync(Guid driverId, double latitude, double longitude, DateTime recordedOnUtc)
    {
        ISubscriber subscriber = Factory.Services.GetRequiredService<IConnectionMultiplexer>().GetSubscriber();

        string payload = JsonSerializer.Serialize(new
        {
            DriverId = driverId,
            Latitude = latitude,
            Longitude = longitude,
            RecordedOnUtc = recordedOnUtc
        });

        return subscriber.PublishAsync(RedisChannel.Literal("delivery:driver-locations"), payload);
    }

    private static async Task<string> GetOrCreateAccessTokenAsync(string email, string password)
    {
        await TokenLock.WaitAsync();

        try
        {
            if (!CachedAccessTokensByEmail.TryGetValue(email, out string? accessToken))
            {
                accessToken = await RequestAccessTokenAsync(email, password);
                CachedAccessTokensByEmail[email] = accessToken;
            }

            return accessToken;
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

using System.Data.Common;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Bogus;
using Dapper;
using FoodDeliveryService.Common.Application.Data;
using FoodDeliveryService.Common.Domain;
using FoodDeliveryService.Modules.Orders.Application.Abstractions.Data;
using FoodDeliveryService.Modules.Orders.Domain.Customers;
using FoodDeliveryService.Modules.Orders.Domain.Restaurants;
using Microsoft.Extensions.DependencyInjection;

namespace FoodDeliveryService.Modules.Orders.IntegrationTests.Abstractions;

[Collection(nameof(IntegrationTestCollection))]
public class BaseIntegrationTest : IDisposable
{
    private const string TokenEndpoint = "http://localhost:18080/connect/token";
    private const string PublicClientId = "fooddeliveryservice-public-client";

    // Every test class shares the one IntegrationTestWebAppFactory (one per collection), so the
    // Administrator token is fetched once and reused.
    private static readonly SemaphoreSlim TokenLock = new(1, 1);
    private static string? _cachedAccessToken;

    // Reads responses tolerant of enums serialized as either their name or their numeric value.
    protected static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

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
    /// Attaches the collection's single seeded Administrator Bearer token to
    /// <see cref="HttpClient"/> so tests drive real, authorized HTTP endpoints.
    /// </summary>
    protected async Task<HttpClient> GetAuthenticatedHttpClientAsync()
    {
        string accessToken = await GetOrCreateAccessTokenAsync();

        HttpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        return HttpClient;
    }

    /// <summary>
    /// Places an order through the real <c>POST orders</c> endpoint as the seeded Administrator
    /// (the customer id therefore equals <see cref="IntegrationTestWebAppFactory.TestUserId"/>),
    /// after seeding the Orders replicas the placement pipeline reads (customer, restaurant, one
    /// available menu item). Returns the new order's id and its server-computed subtotal.
    /// </summary>
    protected async Task<PlacedOrder> PlaceOrderAsync(HttpClient client, int quantity = 2)
    {
        const decimal unitPrice = 12.50m;
        var restaurantId = Guid.NewGuid();
        var menuItemId = Guid.NewGuid();

        await SeedReplicasAsync(restaurantId, menuItemId, unitPrice);

        var body = new
        {
            RestaurantId = restaurantId,
            Items = new[] { new { MenuItemId = menuItemId, Quantity = quantity } },
            DeliveryAddress = new
            {
                Street = Faker.Address.StreetAddress(),
                City = Faker.Address.City(),
                PostalCode = Faker.Address.ZipCode(),
                Country = Faker.Address.Country(),
                Notes = (string?)null,
                Latitude = Faker.Address.Latitude(),
                Longitude = Faker.Address.Longitude()
            },
            PaymentMethod = "CashOnDelivery"
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, "orders")
        {
            Content = JsonContent.Create(body)
        };
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());

        HttpResponseMessage response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        response.EnsureSuccessStatusCode();

        Guid orderId = await response.Content.ReadFromJsonAsync<Guid>(TestContext.Current.CancellationToken);

        return new PlacedOrder(orderId, restaurantId, menuItemId, unitPrice * quantity);
    }

    /// <summary>
    /// Polls the Orders outbox until a domain event of the given type has been written AND processed
    /// without error — proof that the transition raised its event and the outbox job dispatched the
    /// handler that publishes the corresponding integration event.
    /// </summary>
    protected Task<Result<bool>> WaitForProcessedOutboxEventAsync(string eventTypeFragment) =>
        Poller.WaitAsync<bool>(
            TimeSpan.FromSeconds(30),
            async () =>
            {
                await using AsyncServiceScope scope = Factory.Services.CreateAsyncScope();
                var connectionFactory = scope.ServiceProvider.GetRequiredService<IDbConnectionFactory>();

                await using DbConnection connection = await connectionFactory.OpenConnectionAsync();

                const string sql =
                    """
                    SELECT COUNT(*)
                    FROM outbox_messages
                    WHERE type LIKE @Pattern AND processed_on_utc IS NOT NULL AND error IS NULL
                    """;

                long count = await connection.ExecuteScalarAsync<long>(
                    sql,
                    new { Pattern = $"%{eventTypeFragment}%" });

                return count > 0 ? Result.Success(true) : Result.Failure<bool>(Error.NullValue);
            });

    /// <summary>
    /// Self-registers a fresh Customer against the in-process Users host and returns a Bearer token
    /// for it. Its resolved permission set (Customer role) lacks <c>orders:manage</c>, so it is the
    /// vehicle for the manager-transition authorization tests.
    /// </summary>
    protected async Task<string> RegisterCustomerAndGetTokenAsync()
    {
        string email = $"orders-customer+{Guid.NewGuid():N}@fooddeliveryservice.com";
        const string password = "Orders-Customer-P@ssw0rd1";

        HttpClient usersClient = Factory.UsersApi.CreateClient();

        HttpResponseMessage response = await usersClient.PostAsJsonAsync(
            "users/register",
            new { Email = email, Password = password, FirstName = "Cust", LastName = "Omer" },
            TestContext.Current.CancellationToken);

        response.EnsureSuccessStatusCode();

        return await GetAccessTokenAsync(email, password);
    }

    private async Task SeedReplicasAsync(Guid restaurantId, Guid menuItemId, decimal unitPrice)
    {
        await using AsyncServiceScope scope = Factory.Services.CreateAsyncScope();

        var customerRepository = scope.ServiceProvider.GetRequiredService<ICustomerRepository>();
        var restaurantRepository = scope.ServiceProvider.GetRequiredService<IRestaurantReplicaRepository>();
        var menuItemRepository = scope.ServiceProvider.GetRequiredService<IMenuItemReplicaRepository>();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        // Customer replica for the acting Administrator — created once, then shared across tests.
        Customer? existingCustomer =
            await customerRepository.GetAsync(Factory.TestUserId, TestContext.Current.CancellationToken);

        if (existingCustomer is null)
        {
            customerRepository.Insert(
                Customer.Create(Factory.TestUserId, Factory.TestUserEmail, "Orders", "IntegrationTests"));
        }

        // Fresh restaurant + available menu item per order. The restaurant's ManagerUserId is a
        // random id — the Administrator's transition calls pass through the admin-only ownership
        // bypass, not manager equality.
        restaurantRepository.Insert(
            Restaurant.Create(
                restaurantId,
                Guid.NewGuid(),
                Faker.Company.CompanyName(),
                0.15m,
                Faker.Address.Latitude(),
                Faker.Address.Longitude()));

        menuItemRepository.Insert(
            MenuItem.Create(menuItemId, restaurantId, Faker.Commerce.ProductName(), unitPrice, isAvailable: true));

        await unitOfWork.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    private async Task<string> GetOrCreateAccessTokenAsync()
    {
        await TokenLock.WaitAsync(TestContext.Current.CancellationToken);

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

        using var authRequest = new HttpRequestMessage(HttpMethod.Post, new Uri(TokenEndpoint))
        {
            Content = authRequestContent
        };

        using HttpResponseMessage authorizationResponse = await client.SendAsync(authRequest);

        authorizationResponse.EnsureSuccessStatusCode();

        AuthToken authToken = (await authorizationResponse.Content.ReadFromJsonAsync<AuthToken>())!;

        return authToken.AccessToken;
    }

    protected sealed record PlacedOrder(Guid OrderId, Guid RestaurantId, Guid MenuItemId, decimal ExpectedSubtotal);

    private sealed class AuthToken
    {
        [JsonPropertyName("access_token")]
        public string AccessToken { get; init; } = string.Empty;
    }
}

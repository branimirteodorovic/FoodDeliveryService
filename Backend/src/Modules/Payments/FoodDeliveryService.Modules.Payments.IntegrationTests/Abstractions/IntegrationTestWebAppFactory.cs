using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using FoodDeliveryService.Modules.Payments.Application.Abstractions.Payments;
using FoodDeliveryService.Modules.Users.Application.Abstractions.Data;
using FoodDeliveryService.Modules.Users.Domain.Users;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Testcontainers.PostgreSql;
using Testcontainers.RabbitMq;
using Testcontainers.Redis;

namespace FoodDeliveryService.Modules.Payments.IntegrationTests.Abstractions;

/// <summary>
/// The Payments host, its dependencies, and the two other services this feature actually spans.
/// <para>
/// <b><see cref="FakePaymentGateway"/> replaces the Stripe seam and nothing else.</b> Everything
/// above it is the real thing — the real endpoints, the real MediatR pipeline, the real outbox and
/// inbox over a real broker, the real permission RPC. That is the point of §5.2's abstraction: the
/// only component substituted here is the one that would otherwise need a network, an API key and a
/// card whose behaviour Stripe decides.
/// </para>
/// </summary>
public class IntegrationTestWebAppFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private const string IdentityBaseUrl = "http://localhost:18080";
    private const string ConfidentialClientId = "fooddeliveryservice-confidential-client";
    private const string ConfidentialClientSecret = "PzotcrvZRF9BHCKcUxdKfHWlIPECG49k";

    private readonly PostgreSqlContainer _dbContainer = new PostgreSqlBuilder("postgres:17")
        .WithDatabase("fooddeliveryservice_payments")
        .WithUsername("postgres")
        .WithPassword("postgres")
        .Build();

    private readonly RedisContainer _redisContainer = new RedisBuilder("redis:latest")
        .Build();

    private readonly RabbitMqContainer _rabbitMqContainer = new RabbitMqBuilder("rabbitmq:management-alpine")
        .WithUsername("guest")
        .WithPassword("guest")
        .Build();

    private UsersApiTestFactory? _usersApiFactory;

    private OrdersApiTestFactory? _ordersApiFactory;

    /// <summary>
    /// The scripted stand-in for Stripe, shared by the host and the tests. A singleton so a test can
    /// script an outcome, drive an endpoint, and then read back the calls the handler made — which
    /// is how "the gateway was called once, with this idempotency key" is asserted at all.
    /// </summary>
    internal FakePaymentGateway PaymentGateway { get; } = new();

    /// <summary>
    /// The Payments container. Exposed rather than read back from the <c>ConnectionStrings:Database</c>
    /// environment variable, because all three hosts write that same key and the last one to build
    /// wins — a test reading the variable would query whichever database booted last.
    /// </summary>
    public string ConnectionString => _dbContainer.GetConnectionString();

    /// <summary>The in-process Users.Api host — answers the permissions RPC and publishes UserRegistered.</summary>
    internal UsersApiTestFactory UsersApi =>
        _usersApiFactory ?? throw new InvalidOperationException("The Users test host has not been initialized.");

    /// <summary>The in-process Orders.Api host — consumes what Payments publishes.</summary>
    internal OrdersApiTestFactory OrdersApi =>
        _ordersApiFactory ?? throw new InvalidOperationException("The Orders test host has not been initialized.");

    /// <summary>A customer: holds payment-methods:manage and payments:read.</summary>
    public string CustomerUserEmail { get; private set; } = string.Empty;

    /// <summary>A second customer, so "somebody else's card" has a real owner behind it.</summary>
    public string OtherCustomerUserEmail { get; private set; } = string.Empty;

    /// <summary>
    /// A restaurant manager: holds none of the three payment codes. The 403 case — and the reason it
    /// is a real role rather than a token with the claim stripped out is that this asserts the
    /// seeding, not the policy plumbing.
    /// </summary>
    public string ManagerUserEmail { get; private set; } = string.Empty;

    /// <summary>The seeded customer's module-side user id — the key every profile row is written under.</summary>
    public Guid CustomerUserId { get; private set; }

    public Guid OtherCustomerUserId { get; private set; }

    /// <summary>Shared password for every user this suite seeds.</summary>
    public string TestUserPassword { get; } = "Payments-Tests-P@ssw0rd";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // Program.cs reads these in its own top-level statements, evaluated before
        // WebApplicationFactory's deferred configuration would apply — environment variables are the
        // only override that lands in time. Re-asserted here because the Users and Orders hosts,
        // which build first, use the same keys for their own databases.
        Environment.SetEnvironmentVariable("ConnectionStrings:Database", _dbContainer.GetConnectionString());
        Environment.SetEnvironmentVariable(
            "ConnectionStrings:DatabaseMigrations", _dbContainer.GetConnectionString());
        Environment.SetEnvironmentVariable("ConnectionStrings:Cache", _redisContainer.GetConnectionString());
        Environment.SetEnvironmentVariable("ConnectionStrings:Queue", _rabbitMqContainer.GetConnectionString());

        Environment.SetEnvironmentVariable("MessageProcessor:Outbox:IntervalInSeconds", "1");
        Environment.SetEnvironmentVariable("MessageProcessor:Inbox:IntervalInSeconds", "1");

        // appsettings.Development.json points JWT Bearer's metadata address at the docker-internal
        // hostname, which the JWKS fetch cannot resolve from a plain `dotnet test` process — every
        // token would fail signature validation with a generic 401. ValidIssuers already accepts the
        // localhost issuer.
        Environment.SetEnvironmentVariable(
            "Authentication:MetadataAddress",
            $"{IdentityBaseUrl}/.well-known/openid-configuration");

        // No Stripe keys are set, and none are needed: the host runs in Development, where the
        // presence check in Program.cs deliberately skips (Milestone C, §5.6), and the registration
        // below means the IStripeClient that would complain is never resolved.
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IPaymentGateway>();
            services.AddSingleton<IPaymentGateway>(PaymentGateway);
        });
    }

    public async ValueTask InitializeAsync()
    {
        await _dbContainer.StartAsync();
        await _redisContainer.StartAsync();
        await _rabbitMqContainer.StartAsync();

        _usersApiFactory = new UsersApiTestFactory(
            _redisContainer.GetConnectionString(),
            _rabbitMqContainer.GetConnectionString());

        await _usersApiFactory.InitializeAsync();

        _ordersApiFactory = new OrdersApiTestFactory(
            _redisContainer.GetConnectionString(),
            _rabbitMqContainer.GetConnectionString());

        await _ordersApiFactory.InitializeAsync();

        // Every host is built BEFORE the first user is seeded, and strictly one after another.
        //
        // Users first: seeding writes straight to its database, and its receive endpoints must be
        // bound before any test sends the permissions RPC it answers.
        //
        // Payments and Orders second, but still before seeding — the part that is easy to get wrong.
        // Seeding raises UserRegisteredDomainEvent and the Users outbox publishes from it within a
        // second. MassTransit publishes to an exchange, so a message with no queue bound to it is
        // DROPPED, not queued: seed before the consumers exist and the payment profile is simply
        // never created, with nothing anywhere reporting an error. Touching Services forces a build.
        //
        // One after another rather than interleaved because all three read the same env-var keys.
        _ = _usersApiFactory.Services;
        _ = Services;
        _ = _ordersApiFactory.Services;

        (CustomerUserEmail, Guid customerUserId) = await SeedTestUserAsync(Role.Customer);
        CustomerUserId = customerUserId;

        (OtherCustomerUserEmail, Guid otherCustomerUserId) = await SeedTestUserAsync(Role.Customer);
        OtherCustomerUserId = otherCustomerUserId;

        (ManagerUserEmail, _) = await SeedTestUserAsync(Role.RestaurantManager);
    }

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        await _dbContainer.StopAsync();
        await _redisContainer.StopAsync();
        await _rabbitMqContainer.StopAsync();

        if (_usersApiFactory is not null)
        {
            await _usersApiFactory.DisposeAsync();
        }

        if (_ordersApiFactory is not null)
        {
            await _ordersApiFactory.DisposeAsync();
        }
    }

    /// <summary>
    /// Registers one test user per role: a real ASP.NET Identity credential against the locally
    /// running Identity service (docker-compose, not a testcontainer — it must already be up), plus
    /// a matching Users-module row inserted into the Users test host's own ephemeral database. The
    /// module-side row carries the role, and therefore the permissions.
    /// </summary>
    private async Task<(string Email, Guid UserId)> SeedTestUserAsync(Role role)
    {
        // Identity's store is real and persistent, so a fixed email would collide across repeated
        // local runs — a unique one keeps registration idempotent by construction.
        string email = $"payments-tests+{Guid.NewGuid():N}@fooddeliveryservice.com";

        string identityId = await RegisterIdentityUserAsync(email, TestUserPassword);

        await using AsyncServiceScope scope = _usersApiFactory!.Services.CreateAsyncScope();

        var userRepository = scope.ServiceProvider.GetRequiredService<IUserRepository>();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        var user = User.Create(email, "Payments", "IntegrationTests", identityId, role);

        userRepository.Insert(user);

        await unitOfWork.SaveChangesAsync();

        return (email, user.Id);
    }

    private static async Task<string> RegisterIdentityUserAsync(string email, string password)
    {
        using var client = new HttpClient();

        // client_credentials token for the confidential client (users:register scope) — the same
        // mechanism DuendeAuthDelegatingHandler uses in production to call Identity's local API.
        var tokenRequestParameters = new KeyValuePair<string, string>[]
        {
            new("client_id", ConfidentialClientId),
            new("client_secret", ConfidentialClientSecret),
            new("grant_type", "client_credentials"),
            new("scope", "users:register")
        };

        using var tokenRequestContent = new FormUrlEncodedContent(tokenRequestParameters);

        using var tokenRequest = new HttpRequestMessage(HttpMethod.Post, new Uri($"{IdentityBaseUrl}/connect/token"))
        {
            Content = tokenRequestContent
        };

        using HttpResponseMessage tokenResponse = await client.SendAsync(tokenRequest);

        tokenResponse.EnsureSuccessStatusCode();

        ClientCredentialsToken clientCredentialsToken =
            (await tokenResponse.Content.ReadFromJsonAsync<ClientCredentialsToken>())!;

        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", clientCredentialsToken.AccessToken);

        using HttpResponseMessage registerResponse = await client.PostAsJsonAsync(
            $"{IdentityBaseUrl}/api/users",
            new { Email = email, FirstName = "Payments", LastName = "IntegrationTests", Password = password });

        registerResponse.EnsureSuccessStatusCode();

        RegisteredIdentityUser registeredUser =
            (await registerResponse.Content.ReadFromJsonAsync<RegisteredIdentityUser>())!;

        return registeredUser.Id;
    }

    private sealed class ClientCredentialsToken
    {
        [JsonPropertyName("access_token")]
        public string AccessToken { get; init; } = string.Empty;
    }

    private sealed class RegisteredIdentityUser
    {
        [JsonPropertyName("id")]
        public string Id { get; init; } = string.Empty;
    }
}

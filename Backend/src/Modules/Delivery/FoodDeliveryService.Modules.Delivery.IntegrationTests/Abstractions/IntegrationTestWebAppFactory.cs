using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using FoodDeliveryService.Modules.Users.Application.Abstractions.Data;
using FoodDeliveryService.Modules.Users.Domain.Users;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry.Metrics;
using Testcontainers.PostgreSql;
using Testcontainers.RabbitMq;
using Testcontainers.Redis;

namespace FoodDeliveryService.Modules.Delivery.IntegrationTests.Abstractions;

public class IntegrationTestWebAppFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private const string IdentityBaseUrl = "http://localhost:18080";
    private const string ConfidentialClientId = "fooddeliveryservice-confidential-client";
    private const string ConfidentialClientSecret = "PzotcrvZRF9BHCKcUxdKfHWlIPECG49k";

    private readonly PostgreSqlContainer _dbContainer = new PostgreSqlBuilder("postgres:17")
        .WithDatabase("fooddeliveryservice_delivery")
        .WithUsername("postgres")
        .WithPassword("postgres")
        .Build();

    private readonly RedisContainer _redisContainer = new RedisBuilder("redis:latest")
        .Build();

    private readonly RabbitMqContainer _rabbitMqContainer = new RabbitMqBuilder("rabbitmq:management-alpine")
        .WithUsername("guest")
        .WithPassword("guest")
        .Build();

    private readonly List<Metric> _exportedMetrics = [];

    private UsersApiTestFactory? _usersApiFactory;

    /// <summary>
    /// The in-process Users.Api test host — answers the permissions and provisioning RPCs over the
    /// ephemeral broker, exposes the users/accept-invitation endpoint for driver activation, and
    /// lets tests assert the module-side account (invited DeliveryDriver) directly from DI.
    /// </summary>
    internal UsersApiTestFactory UsersApi =>
        _usersApiFactory ?? throw new InvalidOperationException("The Users test host has not been initialized.");

    /// <summary>
    /// Email of the Administrator test user seeded once for the whole run (real Identity credential
    /// + a real Users-module Administrator row) — holds users:provision and deliveries:administer,
    /// so it can onboard drivers and bypass self-only checks.
    /// </summary>
    public string AdminUserEmail { get; private set; } = string.Empty;

    /// <summary>
    /// Email of the Customer test user seeded the same way — holds NO delivery permissions beyond
    /// deliveries:read, so it proves the authorization failures (403 on onboarding).
    /// </summary>
    public string CustomerUserEmail { get; private set; } = string.Empty;

    /// <summary>Shared password for every user this suite seeds or activates.</summary>
    public string TestUserPassword { get; } = "Delivery-Tests-P@ssw0rd";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // Program.cs reads these via builder.Configuration.GetConnectionStringOrThrow(...) in its
        // own top-level statements — evaluated eagerly, before WebApplicationFactory's deferred
        // host builder would apply a ConfigureAppConfiguration override. Environment variables are
        // visible from before Program.Main even runs, so they're the only override that lands in
        // time. This also re-asserts Delivery's own values in case the Users test host (which
        // builds first, using the same env var keys) left its own behind — safe, because that host
        // is already fully built by then.
        Environment.SetEnvironmentVariable("ConnectionStrings:Database", _dbContainer.GetConnectionString());
        Environment.SetEnvironmentVariable("ConnectionStrings:Cache", _redisContainer.GetConnectionString());
        Environment.SetEnvironmentVariable("ConnectionStrings:Queue", _rabbitMqContainer.GetConnectionString());

        // Reduce interval to 1 second to speed up tests.
        Environment.SetEnvironmentVariable("MessageProcessor:Outbox:IntervalInSeconds", "1");
        Environment.SetEnvironmentVariable("MessageProcessor:Inbox:IntervalInSeconds", "1");

        // Shrink the assignment offer window so the timeout → re-offer path is testable in seconds
        // (production default is 30s), and tick the expiry job every second. Still long enough for
        // the accept-path tests to respond to a detected offer before it lapses.
        Environment.SetEnvironmentVariable("Delivery:Assignment:OfferWindowInSeconds", "10");
        Environment.SetEnvironmentVariable("Delivery:Assignment:ExpiredOffersJobIntervalInSeconds", "1");

        // appsettings.Development.json points JWT Bearer's metadata address at the docker-internal
        // hostname (fooddeliveryservice.identity), which the JWKS/discovery fetch can't resolve
        // from a plain "dotnet test" process on the host machine — every token would otherwise fail
        // signature validation with a generic 401. Point it at the same localhost:18080 Identity
        // is reachable at from here (ValidIssuers already accepts that issuer).
        Environment.SetEnvironmentVariable(
            "Authentication:MetadataAddress",
            $"{IdentityBaseUrl}/.well-known/openid-configuration");

        // A second metrics reader alongside the OTLP one AddInfrastructure wires up, so the
        // assignment-metrics test asserts what this host actually exports rather than what a
        // listener attached straight to the meter would see regardless of registration.
        builder.ConfigureServices(services =>
            services.ConfigureOpenTelemetryMeterProvider(metrics =>
                metrics.AddInMemoryExporter(_exportedMetrics)));
    }

    /// <summary>
    /// Collects everything the host's <see cref="MeterProvider"/> has aggregated since the last
    /// call. Metrics are exported on a periodic reader in production, so a test has to force the
    /// collection cycle itself.
    /// </summary>
    public IReadOnlyList<Metric> CollectMetrics()
    {
        _exportedMetrics.Clear();

        Services.GetRequiredService<MeterProvider>().ForceFlush();

        return [.. _exportedMetrics];
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

        // Seeding touches UsersApi.Services, so the Users host is fully built (migrations applied,
        // MassTransit receive endpoints bound) before the Delivery host builds and before any test
        // sends the provisioning/permissions RPCs it is expected to answer.
        AdminUserEmail = await SeedTestUserAsync(Role.Administrator);
        CustomerUserEmail = await SeedTestUserAsync(Role.Customer);
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
    }

    /// <summary>
    /// Registers one test user per role, once per test run: a real ASP.NET Identity credential
    /// against the locally running Identity service (docker-compose, not a testcontainer — must
    /// already be up), plus a matching Users-module row inserted directly into the Users test
    /// host's own (ephemeral) database.
    /// </summary>
    private async Task<string> SeedTestUserAsync(Role role)
    {
        // Identity's ASP.NET Identity store is real and persistent (not a testcontainer), so a
        // fixed email would collide across repeated local runs — a unique one keeps registration
        // idempotent-by-construction and always returns a fresh identityId.
        string email = $"delivery-tests+{Guid.NewGuid():N}@fooddeliveryservice.com";

        string identityId = await RegisterIdentityUserAsync(email, TestUserPassword);

        await using AsyncServiceScope scope = _usersApiFactory!.Services.CreateAsyncScope();

        var userRepository = scope.ServiceProvider.GetRequiredService<IUserRepository>();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        var user = User.Create(email, "Delivery", "IntegrationTests", identityId, role);

        userRepository.Insert(user);

        await unitOfWork.SaveChangesAsync();

        return email;
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
            new { Email = email, FirstName = "Delivery", LastName = "IntegrationTests", Password = password });

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

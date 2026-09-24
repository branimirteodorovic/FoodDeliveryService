using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using FoodDeliveryService.Modules.Users.Application.Abstractions.Data;
using FoodDeliveryService.Modules.Users.Domain.Users;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;
using Testcontainers.RabbitMq;
using Testcontainers.Redis;

namespace FoodDeliveryService.Modules.Users.IntegrationTests.Abstractions;

/// <summary>
/// Fixture for the Users module. The system under test is the real Users.Api host (<c>Program</c>),
/// driven through its full HTTP pipeline (auth → MediatR → EF Core/Dapper → outbox) against
/// ephemeral Postgres/Redis/RabbitMQ testcontainers. Unlike the other modules' fixtures, no separate
/// Users host is spun up for the permission RPC — the Users module owns permissions locally, and its
/// only two HTTP endpoints (register, accept-invitation) are anonymous. An in-process Orders.Api host
/// is started so tests can assert UserRegisteredIntegrationEvent propagates into the Orders Customer
/// replica.
/// </summary>
public class IntegrationTestWebAppFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private const string IdentityBaseUrl = "http://localhost:18080";
    private const string ConfidentialClientId = "fooddeliveryservice-confidential-client";
    private const string ConfidentialClientSecret = "PzotcrvZRF9BHCKcUxdKfHWlIPECG49k";

    /// <summary>
    /// Clears ASP.NET Identity's full default strength policy, which the instance on :18080
    /// enforces — see <c>BaseIntegrationTest.StrongPassword</c> for why a Faker password cannot.
    /// </summary>
    public string AdminPassword { get; } = "Users-Tests-Admin-P@ssw0rd1";

    /// <summary>
    /// The seeded administrator — the only caller holding <c>user-roles:manage</c>, and therefore
    /// the only one who can change anybody's roles.
    /// </summary>
    public string AdminEmail { get; private set; } = string.Empty;

    private readonly PostgreSqlContainer _dbContainer = new PostgreSqlBuilder("postgres:17")
        .WithDatabase("fooddeliveryservice_users")
        .WithUsername("postgres")
        .WithPassword("postgres")
        .Build();

    private readonly RedisContainer _redisContainer = new RedisBuilder("redis:latest")
        .Build();

    private readonly RabbitMqContainer _rabbitMqContainer = new RabbitMqBuilder("rabbitmq:management-alpine")
        .WithUsername("guest")
        .WithPassword("guest")
        .Build();

    private OrdersApiTestFactory? _ordersApiFactory;

    /// <summary>
    /// The in-process Orders.Api test host — lets tests assert cross-service propagation (the Orders
    /// Customer replica materialized from UserRegisteredIntegrationEvent) by resolving Orders' own
    /// services from DI instead of exposing a test-only read endpoint on the Orders API.
    /// </summary>
    internal OrdersApiTestFactory OrdersApi =>
        _ordersApiFactory ?? throw new InvalidOperationException("The Orders test host has not been initialized.");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // Program.cs reads these via builder.Configuration.GetConnectionStringOrThrow(...) in its own
        // top-level statements — evaluated eagerly, before WebApplicationFactory's deferred host
        // builder would apply a ConfigureAppConfiguration override. Environment variables are visible
        // from before Program.Main even runs, so they're the only override that lands in time. This
        // also re-asserts the Users values in case the Orders test host (which builds first, using the
        // same env var keys) left its own behind — safe, because that host is already fully built by
        // the time the first test builds this SUT.
        Environment.SetEnvironmentVariable("ConnectionStrings:Database", _dbContainer.GetConnectionString());
        // Feature 3.7 Milestone C split the migration credential out into its own connection
        // string, and app.ApplyMigrations() reads THAT one. Overriding only Database leaves the
        // migration pointed at appsettings.Development.json's docker-internal host, which a plain
        // `dotnet test` process cannot resolve — the host then dies during startup with a DNS
        // failure and every test in the suite fails before it runs. The fallback inside
        // ApplyMigration only fires when the key is absent, and it is not: it is present and wrong.
        Environment.SetEnvironmentVariable(
            "ConnectionStrings:DatabaseMigrations", _dbContainer.GetConnectionString());

        Environment.SetEnvironmentVariable("ConnectionStrings:Cache", _redisContainer.GetConnectionString());
        Environment.SetEnvironmentVariable("ConnectionStrings:Queue", _rabbitMqContainer.GetConnectionString());

        // Reduce interval to 1 second to speed up outbox publication of the registration event.
        Environment.SetEnvironmentVariable("MessageProcessor:Outbox:IntervalInSeconds", "1");
        Environment.SetEnvironmentVariable("MessageProcessor:Inbox:IntervalInSeconds", "1");

        // appsettings.Development.json points JWT Bearer's metadata address at the docker-internal
        // hostname (fooddeliveryservice.identity), which the JWKS/discovery fetch can't resolve from a
        // plain "dotnet test" process on the host machine. Point it at the same localhost:18080
        // Identity is reachable at from here (ValidIssuers already accepts that issuer).
        Environment.SetEnvironmentVariable(
            "Authentication:MetadataAddress",
            $"{IdentityBaseUrl}/.well-known/openid-configuration");

        // Self-service registration (users/register) actually calls Identity's local API to create the
        // account, via DuendeIdentityClient (client-credentials token). appsettings points these at the
        // docker-internal hostname, unreachable from "dotnet test" — point them at localhost:18080 so
        // the provisioning HTTP call resolves. Without this, registration throws a DNS failure → 500.
        Environment.SetEnvironmentVariable("Duende:AdminUrl", $"{IdentityBaseUrl}/api/");
        Environment.SetEnvironmentVariable("Duende:TokenUrl", $"{IdentityBaseUrl}/connect/token");
    }

    public async ValueTask InitializeAsync()
    {
        await _dbContainer.StartAsync();
        await _redisContainer.StartAsync();
        await _rabbitMqContainer.StartAsync();

        _ordersApiFactory = new OrdersApiTestFactory(
            _redisContainer.GetConnectionString(),
            _rabbitMqContainer.GetConnectionString());

        await _ordersApiFactory.InitializeAsync();

        // WebApplicationFactory builds its host lazily — touch Services now so the Orders host starts
        // (migrations applied, MassTransit receive endpoints bound) before any test publishes the
        // registration event it is expected to consume. Built strictly BEFORE the first test creates
        // the Users SUT client, so the shared env var keys never race (the SUT re-asserts its own
        // values in ConfigureWebHost above).
        _ = _ordersApiFactory.Services;

        // The SUT is built here rather than by the first test, for the same reason and in the same
        // order: seeding the administrator below writes through THIS host's DI and raises
        // UserRegisteredDomainEvent, so its outbox and the Orders consumers must both already
        // exist. Building it here also fixes the env-var ordering deterministically instead of
        // leaving it to whichever test runs first.
        _ = Services;

        // Role.Administrator is deliberately absent from Role.Assignable — nobody can register or
        // be provisioned as one — but User.Create takes a Role directly, so the fixture can seed
        // one. It is the only way to exercise an administrator-gated endpoint here, and it mirrors
        // what the Support fixture does for the same reason.
        AdminEmail = await SeedAdministratorAsync();
    }

    /// <summary>
    /// Creates the credential at the real Identity server and the matching module-side user
    /// holding <see cref="Role.Administrator"/>. Returns the email, which is what the password
    /// grant needs.
    /// </summary>
    private async Task<string> SeedAdministratorAsync()
    {
        // Identity's store is real and persistent (not a testcontainer), so a fixed address would
        // collide across repeated local runs and the registration would fail.
        string email = $"users-tests-admin+{Guid.NewGuid():N}@fooddeliveryservice.com";

        string identityId = await RegisterIdentityUserAsync(email, AdminPassword);

        await using AsyncServiceScope scope = Services.CreateAsyncScope();

        var userRepository = scope.ServiceProvider.GetRequiredService<IUserRepository>();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        var user = User.Create(email, "Users", "IntegrationTests", identityId, Role.Administrator);

        userRepository.Insert(user);

        await unitOfWork.SaveChangesAsync();

        return email;
    }

    /// <summary>
    /// Creates the credential through Identity's local API, using the same client-credentials
    /// mechanism <c>DuendeAuthDelegatingHandler</c> uses in production, and returns its subject id.
    /// </summary>
    private static async Task<string> RegisterIdentityUserAsync(string email, string password)
    {
        using var client = new HttpClient();

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
            new { Email = email, FirstName = "Users", LastName = "IntegrationTests", Password = password });

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

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        await _dbContainer.StopAsync();
        await _redisContainer.StopAsync();
        await _rabbitMqContainer.StopAsync();

        if (_ordersApiFactory is not null)
        {
            await _ordersApiFactory.DisposeAsync();
        }
    }
}

using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using FoodDeliveryService.Modules.Users.Application.Abstractions.Data;
using FoodDeliveryService.Modules.Users.Domain.Users;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;
using Testcontainers.Redis;
using Testcontainers.RabbitMq;

namespace FoodDeliveryService.Modules.RealTime.IntegrationTests.Abstractions;

/// <summary>
/// Boots the Real-Time host in-process against ephemeral Postgres (from Milestone D — the
/// RestaurantManager replica) + Redis + RabbitMQ testcontainers, plus a real in-process Users.Api
/// (its own Postgres) so the permission RPC fired on the authenticated handshake is answered for
/// real. Three test users are seeded once (real Identity credentials on the locally running Identity
/// service + Users-module rows): a plain Administrator (any authenticated user works for the
/// Milestone A–C tests), a RestaurantManager, and a SupportAgent (Milestone D) — reused by every test
/// in the collection.
/// </summary>
public class IntegrationTestWebAppFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private const string IdentityBaseUrl = "http://localhost:18080";
    private const string ConfidentialClientId = "fooddeliveryservice-confidential-client";
    private const string ConfidentialClientSecret = "PzotcrvZRF9BHCKcUxdKfHWlIPECG49k";

    private readonly PostgreSqlContainer _dbContainer = new PostgreSqlBuilder("postgres:17")
        .WithDatabase("fooddeliveryservice_realtime")
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

    /// <summary>
    /// Email/password of the single plain test user seeded once for the whole test run (real Identity
    /// credential + a real Users-module Administrator row) — reused by every Milestone A–C test in
    /// the collection via <see cref="BaseIntegrationTest"/>. The hub is <c>[Authorize]</c> only, so
    /// any authenticated user suffices for those.
    /// </summary>
    public string TestUserEmail { get; private set; } = string.Empty;

    public string TestUserPassword { get; } = "RealTime-Tests-P@ssw0rd";

    /// <summary>
    /// The seeded user's module-side id — the same id CustomClaimsTransformation resolves into the
    /// <c>sub</c> claim (so the connected client lands in <c>user:{TestUserId}</c>) and the same id
    /// space as the <c>CustomerId</c> on Orders' integration events. A Milestone-B test publishes an
    /// order event with <c>CustomerId = TestUserId</c> to prove the frame reaches this customer.
    /// </summary>
    public Guid TestUserId { get; private set; }

    /// <summary>Milestone D: a RestaurantManager-role test user (Permissions.RestaurantDashboard).</summary>
    public string RestaurantManagerEmail { get; private set; } = string.Empty;

    public string RestaurantManagerPassword { get; } = "RealTime-Tests-Manager-P@ssw0rd";

    /// <summary>The manager's module-side id — the key a RestaurantManager replica row is upserted under.</summary>
    public Guid RestaurantManagerUserId { get; private set; }

    /// <summary>Milestone D: a SupportAgent-role test user (Permissions.SupportDashboard).</summary>
    public string SupportAgentEmail { get; private set; } = string.Empty;

    public string SupportAgentPassword { get; } = "RealTime-Tests-Support-P@ssw0rd";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // Program.cs reads these via builder.Configuration.GetConnectionStringOrThrow(...) in its own
        // top-level statements — evaluated eagerly, before WebApplicationFactory's deferred host
        // builder would apply a ConfigureAppConfiguration override. Environment variables are visible
        // from before Program.Main runs, so they're the only override that lands in time.
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

        // appsettings.Development.json points JWT Bearer's metadata address at the docker-internal
        // hostname (fooddeliveryservice.identity), which the JWKS/discovery fetch can't resolve from
        // a plain "dotnet test" process on the host machine — every token would otherwise fail
        // signature validation with a generic 401. Point it at the same localhost:18080 Identity is
        // reachable at from here (ValidIssuers already accepts that issuer).
        Environment.SetEnvironmentVariable(
            "Authentication:MetadataAddress",
            $"{IdentityBaseUrl}/.well-known/openid-configuration");
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

        await SeedTestUsersAsync();

        // WebApplicationFactory builds its host lazily — touch Services now so the Users host starts
        // (migrations applied, MassTransit receive endpoints bound, GetUserPermissionsRequestConsumer
        // listening) before any test opens an authenticated socket that triggers the permission RPC.
        _ = _usersApiFactory.Services;
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
    /// Registers the three test users, once per test run: real ASP.NET Identity credentials against
    /// the locally running Identity service (docker-compose, not a testcontainer — must already be
    /// up), plus matching Users-module rows inserted directly into the Users test host's own
    /// (ephemeral) database — Administrator, RestaurantManager and SupportAgent (Milestone D).
    /// </summary>
    private async Task SeedTestUsersAsync()
    {
        // Identity's ASP.NET Identity store is real and persistent (not a testcontainer), so fixed
        // emails would collide across repeated local runs — unique ones keep registration
        // idempotent-by-construction and always return fresh identityIds.
        TestUserEmail = $"realtime-tests+{Guid.NewGuid():N}@fooddeliveryservice.com";
        RestaurantManagerEmail = $"realtime-tests-manager+{Guid.NewGuid():N}@fooddeliveryservice.com";
        SupportAgentEmail = $"realtime-tests-support+{Guid.NewGuid():N}@fooddeliveryservice.com";

        string identityId = await RegisterIdentityUserAsync(TestUserEmail, TestUserPassword);
        string managerIdentityId = await RegisterIdentityUserAsync(RestaurantManagerEmail, RestaurantManagerPassword);
        string supportIdentityId = await RegisterIdentityUserAsync(SupportAgentEmail, SupportAgentPassword);

        await using var scope = _usersApiFactory!.Services.CreateAsyncScope();

        var userRepository = scope.ServiceProvider.GetRequiredService<IUserRepository>();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        var user = User.Create(TestUserEmail, "RealTime", "IntegrationTests", identityId, Role.Administrator);
        var manager = User.Create(RestaurantManagerEmail, "RealTime", "Manager", managerIdentityId, Role.RestaurantManager);
        var support = User.Create(SupportAgentEmail, "RealTime", "Support", supportIdentityId, Role.SupportAgent);

        userRepository.Insert(user);
        userRepository.Insert(manager);
        userRepository.Insert(support);

        await unitOfWork.SaveChangesAsync();

        TestUserId = user.Id;
        RestaurantManagerUserId = manager.Id;
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
            new { Email = email, FirstName = "RealTime", LastName = "IntegrationTests", Password = password });

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

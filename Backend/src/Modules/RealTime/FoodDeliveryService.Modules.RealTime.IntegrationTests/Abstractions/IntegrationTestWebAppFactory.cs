using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using FoodDeliveryService.Modules.Users.Application.Abstractions.Data;
using FoodDeliveryService.Modules.Users.Domain.Users;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.Redis;
using Testcontainers.RabbitMq;

namespace FoodDeliveryService.Modules.RealTime.IntegrationTests.Abstractions;

/// <summary>
/// Boots the Real-Time host in-process against ephemeral Redis + RabbitMQ testcontainers, plus a
/// real in-process Users.Api (its own Postgres) so the permission RPC fired on the authenticated
/// handshake is answered for real. The Real-Time service owns no database, so — unlike the other
/// suites — there is no Postgres container for the host under test. A single test user is seeded
/// once (real Identity credential on the locally running Identity service + a Users-module
/// Administrator row) and reused by every test in the collection.
/// </summary>
public class IntegrationTestWebAppFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private const string IdentityBaseUrl = "http://localhost:18080";
    private const string ConfidentialClientId = "fooddeliveryservice-confidential-client";
    private const string ConfidentialClientSecret = "PzotcrvZRF9BHCKcUxdKfHWlIPECG49k";

    private readonly RedisContainer _redisContainer = new RedisBuilder("redis:latest")
        .Build();

    private readonly RabbitMqContainer _rabbitMqContainer = new RabbitMqBuilder("rabbitmq:management-alpine")
        .WithUsername("guest")
        .WithPassword("guest")
        .Build();

    private UsersApiTestFactory? _usersApiFactory;

    /// <summary>
    /// Email/password of the single test user seeded once for the whole test run (real Identity
    /// credential + a real Users-module Administrator row) — reused by every test in the collection
    /// via <see cref="BaseIntegrationTest"/>. The hub is <c>[Authorize]</c> only, so any
    /// authenticated user suffices.
    /// </summary>
    public string TestUserEmail { get; private set; } = string.Empty;

    public string TestUserPassword { get; } = "RealTime-Tests-P@ssw0rd";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // Program.cs reads these via builder.Configuration.GetConnectionStringOrThrow(...) in its own
        // top-level statements — evaluated eagerly, before WebApplicationFactory's deferred host
        // builder would apply a ConfigureAppConfiguration override. Environment variables are visible
        // from before Program.Main runs, so they're the only override that lands in time. The
        // Real-Time host reads no "Database" — it owns none. Re-assert Cache/Queue here in case the
        // Users test host (built first, same env keys) left its own values behind.
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
        await _redisContainer.StartAsync();
        await _rabbitMqContainer.StartAsync();

        _usersApiFactory = new UsersApiTestFactory(
            _redisContainer.GetConnectionString(),
            _rabbitMqContainer.GetConnectionString());

        await _usersApiFactory.InitializeAsync();

        await SeedTestUserAsync();

        // WebApplicationFactory builds its host lazily — touch Services now so the Users host starts
        // (migrations applied, MassTransit receive endpoints bound, GetUserPermissionsRequestConsumer
        // listening) before any test opens an authenticated socket that triggers the permission RPC.
        _ = _usersApiFactory.Services;
    }

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        await _redisContainer.StopAsync();
        await _rabbitMqContainer.StopAsync();

        if (_usersApiFactory is not null)
        {
            await _usersApiFactory.DisposeAsync();
        }
    }

    /// <summary>
    /// Registers exactly one test user, once per test run: a real ASP.NET Identity credential
    /// against the locally running Identity service (docker-compose, not a testcontainer — must
    /// already be up), plus a matching Users-module Administrator row inserted directly into the
    /// Users test host's own (ephemeral) database.
    /// </summary>
    private async Task SeedTestUserAsync()
    {
        // Identity's ASP.NET Identity store is real and persistent (not a testcontainer), so a fixed
        // email would collide across repeated local runs — a unique one keeps registration
        // idempotent-by-construction and always returns a fresh identityId.
        TestUserEmail = $"realtime-tests+{Guid.NewGuid():N}@fooddeliveryservice.com";

        string identityId = await RegisterIdentityUserAsync(TestUserEmail, TestUserPassword);

        await using var scope = _usersApiFactory!.Services.CreateAsyncScope();

        var userRepository = scope.ServiceProvider.GetRequiredService<IUserRepository>();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        var user = User.Create(TestUserEmail, "RealTime", "IntegrationTests", identityId, Role.Administrator);

        userRepository.Insert(user);

        await unitOfWork.SaveChangesAsync();
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

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

namespace FoodDeliveryService.Modules.Support.IntegrationTests.Abstractions;

public class IntegrationTestWebAppFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private const string IdentityBaseUrl = "http://localhost:18080";
    private const string ConfidentialClientId = "fooddeliveryservice-confidential-client";
    private const string ConfidentialClientSecret = "PzotcrvZRF9BHCKcUxdKfHWlIPECG49k";

    private readonly PostgreSqlContainer _dbContainer = new PostgreSqlBuilder("postgres:17")
        .WithDatabase("fooddeliveryservice_support")
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
    /// The in-process Users.Api test host — answers the permissions RPC, so every 200/403 in this
    /// suite is the real seeded permission set rather than a stub.
    /// </summary>
    internal UsersApiTestFactory UsersApi =>
        _usersApiFactory ?? throw new InvalidOperationException("The Users test host has not been initialized.");

    /// <summary>
    /// A support agent: holds support-tickets:read/manage/assign. Notably NOT support-tickets:open,
    /// which is the customer-facing code — the seeding decided that deliberately.
    /// </summary>
    public string AgentUserEmail { get; private set; } = string.Empty;

    /// <summary>A customer: holds support-tickets:open and :read, and nothing else here.</summary>
    public string CustomerUserEmail { get; private set; } = string.Empty;

    /// <summary>A second customer, so the "someone else's ticket is a 404" case is real.</summary>
    public string OtherCustomerUserEmail { get; private set; } = string.Empty;

    /// <summary>Shared password for every user this suite seeds.</summary>
    public string TestUserPassword { get; } = "Support-Tests-P@ssw0rd";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // Program.cs reads these via builder.Configuration.GetConnectionStringOrThrow(...) in its
        // own top-level statements — evaluated eagerly, before WebApplicationFactory's deferred host
        // builder would apply a ConfigureAppConfiguration override. Environment variables are
        // visible from before Program.Main even runs, so they are the only override that lands in
        // time. This also re-asserts Support's own values in case the Users test host (which builds
        // first, using the same env var keys) left its own behind.
        Environment.SetEnvironmentVariable("ConnectionStrings:Database", _dbContainer.GetConnectionString());
        Environment.SetEnvironmentVariable("ConnectionStrings:Cache", _redisContainer.GetConnectionString());
        Environment.SetEnvironmentVariable("ConnectionStrings:Queue", _rabbitMqContainer.GetConnectionString());

        // Reduce the interval to 1 second so the outbox assertion does not wait on production timing.
        Environment.SetEnvironmentVariable("MessageProcessor:Outbox:IntervalInSeconds", "1");
        Environment.SetEnvironmentVariable("MessageProcessor:Inbox:IntervalInSeconds", "1");

        // appsettings.Development.json points JWT Bearer's metadata address at the docker-internal
        // hostname (fooddeliveryservice.identity), which the JWKS/discovery fetch cannot resolve
        // from a plain "dotnet test" process on the host machine — every token would otherwise fail
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

        // Seeding touches UsersApi.Services, so the Users host is fully built (migrations applied,
        // MassTransit receive endpoints bound) before the Support host builds and before any test
        // sends the permissions RPC it is expected to answer.
        AgentUserEmail = await SeedTestUserAsync(Role.SupportAgent);
        CustomerUserEmail = await SeedTestUserAsync(Role.Customer);
        OtherCustomerUserEmail = await SeedTestUserAsync(Role.Customer);
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
    /// Registers one test user per role: a real ASP.NET Identity credential against the locally
    /// running Identity service (docker-compose, not a testcontainer — it must already be up), plus
    /// a matching Users-module row inserted directly into the Users test host's own ephemeral
    /// database. The module-side row is what carries the role, and therefore the permissions.
    /// </summary>
    private async Task<string> SeedTestUserAsync(Role role)
    {
        // Identity's store is real and persistent (not a testcontainer), so a fixed email would
        // collide across repeated local runs — a unique one keeps registration
        // idempotent-by-construction and always returns a fresh identityId.
        string email = $"support-tests+{Guid.NewGuid():N}@fooddeliveryservice.com";

        string identityId = await RegisterIdentityUserAsync(email, TestUserPassword);

        await using AsyncServiceScope scope = _usersApiFactory!.Services.CreateAsyncScope();

        var userRepository = scope.ServiceProvider.GetRequiredService<IUserRepository>();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        var user = User.Create(email, "Support", "IntegrationTests", identityId, role);

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
            new { Email = email, FirstName = "Support", LastName = "IntegrationTests", Password = password });

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

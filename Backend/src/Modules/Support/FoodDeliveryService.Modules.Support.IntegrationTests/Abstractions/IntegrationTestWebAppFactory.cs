using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using FoodDeliveryService.Common.Application.EventBus;
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

    private NotificationsApiTestFactory? _notificationsApiFactory;

    /// <summary>
    /// The in-process Users.Api test host — answers the permissions RPC, so every 200/403 in this
    /// suite is the real seeded permission set rather than a stub.
    /// </summary>
    internal UsersApiTestFactory UsersApi =>
        _usersApiFactory ?? throw new InvalidOperationException("The Users test host has not been initialized.");

    /// <summary>
    /// The in-process Notifications.Api test host — consumes what Support publishes, so a customer
    /// notification is asserted where it is actually written rather than by trusting the publisher.
    /// </summary>
    internal NotificationsApiTestFactory NotificationsApi =>
        _notificationsApiFactory
        ?? throw new InvalidOperationException("The Notifications test host has not been initialized.");

    /// <summary>
    /// A support agent: holds support-tickets:read/manage/assign. Notably NOT support-tickets:open,
    /// which is the customer-facing code — the seeding decided that deliberately.
    /// </summary>
    public string AgentUserEmail { get; private set; } = string.Empty;

    /// <summary>A customer: holds support-tickets:open and :read, and nothing else here.</summary>
    public string CustomerUserEmail { get; private set; } = string.Empty;

    /// <summary>A second customer, so the "someone else's ticket is a 404" case is real.</summary>
    public string OtherCustomerUserEmail { get; private set; } = string.Empty;

    /// <summary>
    /// A second support agent, so "assigning a ticket to somebody else" has a real target and the
    /// agent replica has more than one row to pick the wrong one from.
    /// </summary>
    public string OtherAgentUserEmail { get; private set; } = string.Empty;

    /// <summary>
    /// An administrator. The only caller holding support-tickets:administer, which is what
    /// separates routing another agent's ticket from claiming your own.
    /// </summary>
    public string AdminUserEmail { get; private set; } = string.Empty;

    /// <summary>The seeded agent's module-side user id — the assignment target these tests name.</summary>
    public Guid AgentUserId { get; private set; }

    /// <summary>The second agent's user id.</summary>
    public Guid OtherAgentUserId { get; private set; }

    /// <summary>
    /// The seeded customer's user id — the recipient a notification row is keyed on in the
    /// Notifications database, and the owner of every ticket the customer client opens.
    /// </summary>
    public Guid CustomerUserId { get; private set; }

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

        _notificationsApiFactory = new NotificationsApiTestFactory(
            _redisContainer.GetConnectionString(),
            _rabbitMqContainer.GetConnectionString());

        await _notificationsApiFactory.InitializeAsync();

        // Both hosts are built BEFORE the first user is seeded, and the order matters in both
        // directions.
        //
        // Users first: seeding writes straight to its database, and its MassTransit receive
        // endpoints have to be bound before any test sends the permissions RPC it answers.
        //
        // Support second, but still before seeding — this is the part that is easy to get wrong.
        // Seeding a user raises UserRegisteredDomainEvent, and the Users outbox job publishes the
        // integration event from it within a second. MassTransit publishes to an exchange, so a
        // message with no queue bound to it is dropped, not queued: seed before Support's consumers
        // exist and the agent replica is simply never built, with nothing anywhere reporting an
        // error. Touching Services is what forces each host to build.
        _ = _usersApiFactory.Services;
        _ = Services;

        // Notifications last, but still before seeding, and for the same reason: it builds its
        // recipient replica from the very UserRegistered events seeding raises, and a message
        // published to an exchange with no bound queue is dropped rather than kept. Built after
        // Support because every host reads the same env-var keys — they must build strictly one
        // after another, never interleaved.
        _ = _notificationsApiFactory.Services;

        (AgentUserEmail, Guid agentUserId) = await SeedTestUserAsync(Role.SupportAgent);
        AgentUserId = agentUserId;

        (OtherAgentUserEmail, Guid otherAgentUserId) = await SeedTestUserAsync(Role.SupportAgent);
        OtherAgentUserId = otherAgentUserId;

        // Role.Administrator is deliberately absent from Role.Assignable — nobody can be provisioned
        // as one — but User.Create takes a Role directly, so the test host can seed one. That is the
        // only way to exercise the administrator bypass without a seeded-admin bootstrap here.
        (AdminUserEmail, _) = await SeedTestUserAsync(Role.Administrator);

        (CustomerUserEmail, Guid customerUserId) = await SeedTestUserAsync(Role.Customer);
        CustomerUserId = customerUserId;
        (OtherCustomerUserEmail, _) = await SeedTestUserAsync(Role.Customer);
    }

    /// <summary>
    /// Publishes an upstream integration event onto the shared broker through Support's own
    /// <see cref="IEventBus"/> — the same MassTransit path the owning service uses — so this
    /// module's registered <c>IntegrationEventConsumer&lt;T&gt;</c> receives it into the inbox.
    /// It is how a test gives Support an order to refund without standing up Orders as well.
    /// </summary>
    public async Task PublishAsync<T>(T integrationEvent, CancellationToken cancellationToken = default)
        where T : IIntegrationEvent
    {
        await using AsyncServiceScope scope = Services.CreateAsyncScope();

        var eventBus = scope.ServiceProvider.GetRequiredService<IEventBus>();

        await eventBus.PublishAsync(integrationEvent, cancellationToken);
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

        if (_notificationsApiFactory is not null)
        {
            await _notificationsApiFactory.DisposeAsync();
        }
    }

    /// <summary>
    /// Registers one test user per role: a real ASP.NET Identity credential against the locally
    /// running Identity service (docker-compose, not a testcontainer — it must already be up), plus
    /// a matching Users-module row inserted directly into the Users test host's own ephemeral
    /// database. The module-side row is what carries the role, and therefore the permissions.
    /// </summary>
    private async Task<(string Email, Guid UserId)> SeedTestUserAsync(Role role)
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

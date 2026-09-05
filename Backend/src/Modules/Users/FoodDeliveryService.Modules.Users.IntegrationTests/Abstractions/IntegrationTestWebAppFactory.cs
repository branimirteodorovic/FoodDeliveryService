using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
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

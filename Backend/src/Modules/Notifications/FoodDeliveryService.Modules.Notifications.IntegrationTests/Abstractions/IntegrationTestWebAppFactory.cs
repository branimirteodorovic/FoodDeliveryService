using FoodDeliveryService.Common.Application.EventBus;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;
using Testcontainers.RabbitMq;
using Testcontainers.Redis;

namespace FoodDeliveryService.Modules.Notifications.IntegrationTests.Abstractions;

/// <summary>
/// Fixture hosting the real Notifications.Api in-process against ephemeral Postgres/Redis/RabbitMQ
/// testcontainers. Notifications is a <b>pure event consumer</b> — it exposes no HTTP endpoints and
/// performs no request-time authorization — so, unlike the Restaurants/Users integration tests, this
/// fixture needs no local Identity service, no seeded JWT user, and no in-process Users/Orders hosts.
/// Tests drive the module by publishing upstream integration events onto the shared broker via the
/// SUT's own <see cref="IEventBus"/> (exactly as the owning service would in production) and assert
/// the module's reaction — the RecipientUser replica and the notification-log rows — through its DI.
/// </summary>
public class IntegrationTestWebAppFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly PostgreSqlContainer _dbContainer = new PostgreSqlBuilder("postgres:17")
        .WithDatabase("fooddeliveryservice_notifications")
        .WithUsername("postgres")
        .WithPassword("postgres")
        .Build();

    private readonly RedisContainer _redisContainer = new RedisBuilder("redis:latest")
        .Build();

    private readonly RabbitMqContainer _rabbitMqContainer = new RabbitMqBuilder("rabbitmq:management-alpine")
        .WithUsername("guest")
        .WithPassword("guest")
        .Build();

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

        // Reduce the outbox/inbox poll interval to 1 second so consumed events materialize quickly.
        Environment.SetEnvironmentVariable("MessageProcessor:Outbox:IntervalInSeconds", "1");
        Environment.SetEnvironmentVariable("MessageProcessor:Inbox:IntervalInSeconds", "1");
    }

    public async ValueTask InitializeAsync()
    {
        await _dbContainer.StartAsync();
        await _redisContainer.StartAsync();
        await _rabbitMqContainer.StartAsync();

        // WebApplicationFactory builds its host lazily — touch Services now so the host starts
        // (migrations applied, MassTransit receive endpoints bound) before any test publishes an
        // integration event it is expected to consume.
        _ = Services;
    }

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        await _dbContainer.StopAsync();
        await _redisContainer.StopAsync();
        await _rabbitMqContainer.StopAsync();
    }

    /// <summary>
    /// Publishes an upstream integration event onto the shared broker through the Notifications host's
    /// own <see cref="IEventBus"/> — the same MassTransit publish path the owning service uses — so the
    /// module's registered <c>IntegrationEventConsumer&lt;T&gt;</c> receives it into the inbox.
    /// </summary>
    public async Task PublishAsync<T>(T integrationEvent, CancellationToken cancellationToken = default)
        where T : IIntegrationEvent
    {
        await using AsyncServiceScope scope = Services.CreateAsyncScope();

        var eventBus = scope.ServiceProvider.GetRequiredService<IEventBus>();

        await eventBus.PublishAsync(integrationEvent, cancellationToken);
    }
}

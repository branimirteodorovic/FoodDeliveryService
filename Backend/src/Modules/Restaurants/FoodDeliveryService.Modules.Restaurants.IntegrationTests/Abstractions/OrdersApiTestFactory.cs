extern alias OrdersApi;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Testcontainers.PostgreSql;

namespace FoodDeliveryService.Modules.Restaurants.IntegrationTests.Abstractions;

/// <summary>
/// Hosts a real Orders.Api instance in-process so tests can verify cross-service propagation the
/// production way: Restaurants publishes RestaurantRegisteredIntegrationEvent (outbox → RabbitMQ)
/// and this host's real IntegrationEventConsumer → inbox → UpsertRestaurantCommand pipeline
/// materializes the Restaurant replica — no test-only read endpoint is exposed on Orders for this.
/// Owns its own Postgres testcontainer, but reuses the Redis/RabbitMQ testcontainers
/// <see cref="IntegrationTestWebAppFactory"/> already started, so events flow over the same
/// isolated, ephemeral broker as Restaurants' host.
/// </summary>
internal sealed class OrdersApiTestFactory(string redisConnectionString, string rabbitMqConnectionString)
    : WebApplicationFactory<OrdersApi::Program>, IAsyncLifetime
{
    private readonly PostgreSqlContainer _dbContainer = new PostgreSqlBuilder("postgres:17")
        .WithDatabase("fooddeliveryservice_orders")
        .WithUsername("postgres")
        .WithPassword("postgres")
        .Build();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // Program.cs reads these via builder.Configuration.GetConnectionStringOrThrow(...) in its
        // own top-level statements, evaluated eagerly before WebApplicationFactory's deferred host
        // builder applies ConfigureAppConfiguration overrides — environment variables are the only
        // override that's visible in time, because they're already in the process environment
        // before Program.Main runs at all (same reason IntegrationTestWebAppFactory uses them).
        Environment.SetEnvironmentVariable("ConnectionStrings:Database", _dbContainer.GetConnectionString());
        Environment.SetEnvironmentVariable("ConnectionStrings:Cache", redisConnectionString);
        Environment.SetEnvironmentVariable("ConnectionStrings:Queue", rabbitMqConnectionString);

        // Reduce interval to 1 second so this host's inbox processor materializes the Restaurant
        // replica quickly — the Restaurants host sets these same keys, but only when *its* host
        // builds, which is after this Orders host is already built (see InitializeAsync ordering),
        // so this host would otherwise fall back to the default (slower) interval.
        Environment.SetEnvironmentVariable("MessageProcessor:Outbox:IntervalInSeconds", "1");
        Environment.SetEnvironmentVariable("MessageProcessor:Inbox:IntervalInSeconds", "1");
    }

    public async ValueTask InitializeAsync()
    {
        await _dbContainer.StartAsync();
    }

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        await _dbContainer.StopAsync();
    }
}

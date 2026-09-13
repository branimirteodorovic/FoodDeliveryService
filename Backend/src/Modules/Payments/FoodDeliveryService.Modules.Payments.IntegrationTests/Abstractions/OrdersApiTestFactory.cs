extern alias OrdersApi;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Testcontainers.PostgreSql;

namespace FoodDeliveryService.Modules.Payments.IntegrationTests.Abstractions;

/// <summary>
/// Hosts a real Orders.Api instance in-process so the <c>CanPayByCard</c> replica (§6.3) is asserted
/// where it is actually written, rather than by trusting that Payments published the event.
/// <para>
/// The half of a replication that goes wrong is almost never the publish — it is a consumer that was
/// never registered, a handler in the wrong assembly, or a projection that only ever converges
/// upwards. None of those is visible from the publishing side, and all of them are visible in this
/// host's database.
/// </para>
/// </summary>
internal sealed class OrdersApiTestFactory(string redisConnectionString, string rabbitMqConnectionString)
    : WebApplicationFactory<OrdersApi::Program>, IAsyncLifetime
{
    private readonly PostgreSqlContainer _dbContainer = new PostgreSqlBuilder("postgres:17")
        .WithDatabase("fooddeliveryservice_orders")
        .WithUsername("postgres")
        .WithPassword("postgres")
        .Build();

    /// <summary>
    /// The Orders database, for reading the replica directly. Dapper against the container rather
    /// than this host's own services: the replica is a table, and the assertion should fail if the
    /// row is missing regardless of what the module's repositories would have said about it.
    /// </summary>
    public string ConnectionString => _dbContainer.GetConnectionString();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // See UsersApiTestFactory for why these are environment variables and not configuration
        // overrides — Program.cs reads them eagerly in its top-level statements.
        Environment.SetEnvironmentVariable("ConnectionStrings:Database", _dbContainer.GetConnectionString());
        Environment.SetEnvironmentVariable(
            "ConnectionStrings:DatabaseMigrations", _dbContainer.GetConnectionString());
        Environment.SetEnvironmentVariable("ConnectionStrings:Cache", redisConnectionString);
        Environment.SetEnvironmentVariable("ConnectionStrings:Queue", rabbitMqConnectionString);

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

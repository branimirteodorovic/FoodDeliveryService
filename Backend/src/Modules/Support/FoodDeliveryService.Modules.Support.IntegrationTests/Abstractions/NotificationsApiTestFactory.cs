extern alias NotificationsApi;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Testcontainers.PostgreSql;

namespace FoodDeliveryService.Modules.Support.IntegrationTests.Abstractions;

/// <summary>
/// Hosts a real Notifications.Api instance in-process, so the customer-notification half of the
/// ticket thread is asserted end to end: Support's outbox publishes, the broker delivers, the
/// Notifications inbox consumes, and a notification row appears in <em>that</em> service's database.
/// <para>
/// Worth a third Postgres container because the rule being tested spans the boundary. That only a
/// customer-visible agent message leaves Support cannot be proven inside Support — a test that
/// asserted "no integration event was published" would be checking the same filter the production
/// code reads from. Checking that no email was logged on the other side of the broker is the version
/// of that claim a leak could actually fail.
/// </para>
/// <para>
/// Owns its own Postgres testcontainer and reuses the Redis/RabbitMQ ones
/// <see cref="IntegrationTestWebAppFactory"/> already started, so it shares the broker with Support
/// and with the Users host whose registration events build its recipient replica.
/// </para>
/// </summary>
internal sealed class NotificationsApiTestFactory(string redisConnectionString, string rabbitMqConnectionString)
    : WebApplicationFactory<NotificationsApi::Program>, IAsyncLifetime
{
    private readonly PostgreSqlContainer _dbContainer = new PostgreSqlBuilder("postgres:17")
        .WithDatabase("fooddeliveryservice_notifications")
        .WithUsername("postgres")
        .WithPassword("postgres")
        .Build();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // Environment variables for the same reason every other factory here uses them: Program.cs
        // reads ConnectionStrings:* eagerly in its top-level statements, before WebApplicationFactory
        // would apply a ConfigureAppConfiguration override.
        Environment.SetEnvironmentVariable("ConnectionStrings:Database", _dbContainer.GetConnectionString());
        Environment.SetEnvironmentVariable("ConnectionStrings:Cache", redisConnectionString);
        Environment.SetEnvironmentVariable("ConnectionStrings:Queue", rabbitMqConnectionString);

        // This host sits at the end of two hops — Support's outbox, then its own inbox — so the
        // production interval would put a notification assertion minutes away.
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

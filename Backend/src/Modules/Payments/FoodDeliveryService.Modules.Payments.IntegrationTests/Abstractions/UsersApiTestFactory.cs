extern alias UsersApi;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Testcontainers.PostgreSql;

namespace FoodDeliveryService.Modules.Payments.IntegrationTests.Abstractions;

/// <summary>
/// Hosts a real Users.Api instance in-process so Payments' <c>IPermissionService</c> RPC
/// (<c>GetUserPermissionsRequest</c>) is answered by the real consumer instead of a fake. That is
/// what makes every 200 and 403 in this suite mean something: the permission set is the one Users
/// actually seeds for the role, resolved over the real broker.
/// <para>
/// It is also the publisher of <c>UserRegisteredIntegrationEvent</c>, which is how a
/// <c>CustomerPaymentProfile</c> comes to exist at all (§6.2) — so this host is not only the
/// authorization oracle here, it is half the feature under test.
/// </para>
/// <para>
/// Owns its own Postgres testcontainer and reuses the Redis/RabbitMQ containers
/// <see cref="IntegrationTestWebAppFactory"/> already started, so the RPC and the events travel the
/// same isolated broker as the Payments host's.
/// </para>
/// </summary>
internal sealed class UsersApiTestFactory(string redisConnectionString, string rabbitMqConnectionString)
    : WebApplicationFactory<UsersApi::Program>, IAsyncLifetime
{
    private const string IdentityBaseUrl = "http://localhost:18080";

    private readonly PostgreSqlContainer _dbContainer = new PostgreSqlBuilder("postgres:17")
        .WithDatabase("fooddeliveryservice_users")
        .WithUsername("postgres")
        .WithPassword("postgres")
        .Build();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // Program.cs reads these via builder.Configuration.GetConnectionStringOrThrow(...) in its own
        // top-level statements, evaluated eagerly before WebApplicationFactory's deferred host builder
        // applies ConfigureAppConfiguration overrides — environment variables are the only override
        // visible in time, because they are in the process environment before Program.Main runs.
        Environment.SetEnvironmentVariable("ConnectionStrings:Database", _dbContainer.GetConnectionString());

        // Feature 3.7 Milestone C split the migration credential into its own connection string, and
        // app.ApplyMigrations() reads THAT one. Overriding only Database leaves the migration pointed
        // at appsettings.Development.json's docker-internal host, which a plain `dotnet test` process
        // cannot resolve — the host then dies during startup with a DNS failure and every test fails
        // before it runs. The fallback inside ApplyMigrations fires only when the key is absent, and
        // it is not absent: it is present and wrong.
        Environment.SetEnvironmentVariable(
            "ConnectionStrings:DatabaseMigrations", _dbContainer.GetConnectionString());
        Environment.SetEnvironmentVariable("ConnectionStrings:Cache", redisConnectionString);
        Environment.SetEnvironmentVariable("ConnectionStrings:Queue", rabbitMqConnectionString);

        // Seeding a user raises UserRegisteredDomainEvent, and it is this host's outbox job that
        // turns it into the integration event Payments builds a profile from. At the production
        // interval the profile would not exist for most of a test run. Set here as well as in
        // IntegrationTestWebAppFactory because this host is built first.
        Environment.SetEnvironmentVariable("MessageProcessor:Outbox:IntervalInSeconds", "1");
        Environment.SetEnvironmentVariable("MessageProcessor:Inbox:IntervalInSeconds", "1");

        // appsettings.Development.json points the Duende provisioning client at the docker-internal
        // hostname, which a plain `dotnet test` process cannot resolve. Payments provisions nobody,
        // but this host resolves the client at startup, so it has to point somewhere reachable.
        Environment.SetEnvironmentVariable("Duende:AdminUrl", $"{IdentityBaseUrl}/api/");
        Environment.SetEnvironmentVariable("Duende:TokenUrl", $"{IdentityBaseUrl}/connect/token");
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

extern alias UsersApi;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Testcontainers.PostgreSql;

namespace FoodDeliveryService.Modules.Delivery.IntegrationTests.Abstractions;

/// <summary>
/// Hosts a real Users.Api instance in-process so Delivery's <c>IPermissionService</c> RPC
/// (GetUserPermissionsRequest) and the onboarding <c>ProvisionUserRequest</c>/<c>
/// DeactivateProvisionedUserRequest</c> RPCs are answered by the real consumers instead of fakes.
/// Owns its own Postgres testcontainer, but reuses the Redis/RabbitMQ testcontainers
/// <see cref="IntegrationTestWebAppFactory"/> already started, so the RPCs round-trip over the
/// same isolated, ephemeral broker as Delivery's host.
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
        // Program.cs reads these via builder.Configuration.GetConnectionStringOrThrow(...) in its
        // own top-level statements, evaluated eagerly before WebApplicationFactory's deferred host
        // builder applies ConfigureAppConfiguration overrides — environment variables are the only
        // override that's visible in time, because they're already in the process environment
        // before Program.Main runs at all (same reason IntegrationTestWebAppFactory uses them).
        Environment.SetEnvironmentVariable("ConnectionStrings:Database", _dbContainer.GetConnectionString());

        // Feature 3.7 Milestone C split the migration credential out into its own connection
        // string, and app.ApplyMigrations() reads THAT one. Overriding only Database leaves the
        // migration pointed at appsettings.Development.json's docker-internal host, which a plain
        // `dotnet test` process cannot resolve — the host then dies during startup with a DNS
        // failure and every test in the suite fails before it runs. The fallback inside
        // ApplyMigration only fires when the key is absent, and it is not: it is present and wrong.
        Environment.SetEnvironmentVariable(
            "ConnectionStrings:DatabaseMigrations", _dbContainer.GetConnectionString());
        Environment.SetEnvironmentVariable("ConnectionStrings:Cache", redisConnectionString);
        Environment.SetEnvironmentVariable("ConnectionStrings:Queue", rabbitMqConnectionString);

        // appsettings.Development.json points the Duende provisioning client at the docker-internal
        // hostname (fooddeliveryservice.identity:8080), which a plain "dotnet test" process can't
        // resolve. Driver onboarding runs the REAL ProvisionUserRequest RPC (no fake), so this host
        // actually calls Identity's local API to create the invited account — it must reach
        // Identity at the same localhost:18080 the Delivery host and SeedTestUsersAsync already
        // use. Without this, the invited-registration HTTP call throws a DNS failure, faults the
        // RPC, and onboarding returns 500.
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

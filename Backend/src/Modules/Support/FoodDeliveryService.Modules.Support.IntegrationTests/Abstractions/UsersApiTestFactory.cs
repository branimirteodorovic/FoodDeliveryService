extern alias UsersApi;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Testcontainers.PostgreSql;

namespace FoodDeliveryService.Modules.Support.IntegrationTests.Abstractions;

/// <summary>
/// Hosts a real Users.Api instance in-process so Support's <c>IPermissionService</c> RPC
/// (GetUserPermissionsRequest) is answered by the real consumer instead of a fake — which is what
/// makes the authorization assertions in this suite mean anything: a 403 here is the real permission
/// set resolved over the real broker, not a stub returning what the test wanted.
/// <para>
/// Owns its own Postgres testcontainer, but reuses the Redis/RabbitMQ testcontainers
/// <see cref="IntegrationTestWebAppFactory"/> already started, so the RPC round-trips over the same
/// isolated, ephemeral broker as Support's host.
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
        // Program.cs reads these via builder.Configuration.GetConnectionStringOrThrow(...) in its
        // own top-level statements, evaluated eagerly before WebApplicationFactory's deferred host
        // builder applies ConfigureAppConfiguration overrides — environment variables are the only
        // override that is visible in time, because they are already in the process environment
        // before Program.Main runs at all (same reason IntegrationTestWebAppFactory uses them).
        Environment.SetEnvironmentVariable("ConnectionStrings:Database", _dbContainer.GetConnectionString());
        Environment.SetEnvironmentVariable("ConnectionStrings:Cache", redisConnectionString);
        Environment.SetEnvironmentVariable("ConnectionStrings:Queue", rabbitMqConnectionString);

        // appsettings.Development.json points the Duende provisioning client at the docker-internal
        // hostname (fooddeliveryservice.identity:8080), which a plain "dotnet test" process cannot
        // resolve. Support does not provision anyone, but the Users host resolves this client at
        // startup, so it has to point somewhere reachable.
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

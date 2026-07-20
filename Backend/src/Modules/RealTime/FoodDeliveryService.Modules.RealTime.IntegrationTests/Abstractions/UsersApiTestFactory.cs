extern alias UsersApi;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Testcontainers.PostgreSql;

namespace FoodDeliveryService.Modules.RealTime.IntegrationTests.Abstractions;

/// <summary>
/// Hosts a real Users.Api instance in-process so the Real-Time host's <c>IPermissionService</c> RPC
/// (GetUserPermissionsRequest, MassTransit request/response — fired by CustomClaimsTransformation on
/// the authenticated hub handshake) is answered by the real GetUserPermissionsRequestConsumer
/// instead of a fake. Owns its own Postgres testcontainer, but reuses the Redis/RabbitMQ
/// testcontainers <see cref="IntegrationTestWebAppFactory"/> already started, so the RPC round-trips
/// over the same isolated, ephemeral broker as the Real-Time host.
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
        Environment.SetEnvironmentVariable("ConnectionStrings:Cache", redisConnectionString);
        Environment.SetEnvironmentVariable("ConnectionStrings:Queue", rabbitMqConnectionString);

        // appsettings.Development.json points the Duende provisioning client at the docker-internal
        // hostname (fooddeliveryservice.identity:8080), which a plain "dotnet test" process can't
        // resolve. Point it at the same localhost:18080 the seeding flow uses so any provisioning
        // path resolves Identity correctly.
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

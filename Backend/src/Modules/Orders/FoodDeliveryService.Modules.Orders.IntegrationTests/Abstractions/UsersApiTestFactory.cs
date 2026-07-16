extern alias UsersApi;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Testcontainers.PostgreSql;

namespace FoodDeliveryService.Modules.Orders.IntegrationTests.Abstractions;

/// <summary>
/// Hosts a real Users.Api instance in-process so Orders' <c>IPermissionService</c> RPC
/// (GetUserPermissionsRequest, MassTransit request/response) is answered by the real
/// GetUserPermissionsRequestConsumer instead of a fake. Owns its own Postgres testcontainer, but
/// reuses the Redis/RabbitMQ testcontainers <see cref="IntegrationTestWebAppFactory"/> already
/// started, so the RPC round-trips over the same isolated, ephemeral broker as Orders' host.
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

        // Self-registration of a customer (used by the authorization tests) runs the real Duende
        // client against Identity's local API — point it at the same localhost:18080 the rest of the
        // suite uses, since appsettings targets the docker-internal hostname a "dotnet test" process
        // can't resolve.
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

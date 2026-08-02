using System.Diagnostics;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using FoodDeliveryService.Common.Infrastructure.Diagnostics;
using FoodDeliveryService.Modules.Orders.IntegrationTests.Telemetry;
using FoodDeliveryService.Modules.Users.Application.Abstractions.Data;
using FoodDeliveryService.Modules.Users.Domain.Users;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;
using Serilog;
using Serilog.Events;
using Serilog.Extensions.Logging;
using Testcontainers.PostgreSql;
using Testcontainers.Redis;
using Testcontainers.RabbitMq;

namespace FoodDeliveryService.Modules.Orders.IntegrationTests.Abstractions;

/// <summary>
/// The system-under-test is the Orders.Api host (<c>Program</c>). A real Users.Api host runs
/// alongside it so the permission RPC resolves real permissions; the JWT `sub` the Orders module
/// sees is therefore the Users-module <see cref="User.Id"/> (see <c>CustomClaimsTransformation</c>).
/// That id is exposed as <see cref="TestUserId"/> so tests can seed the matching Orders replicas
/// (customer / restaurant / menu item) the placement pipeline reads.
/// </summary>
public class IntegrationTestWebAppFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private const string IdentityBaseUrl = "http://localhost:18080";
    private const string ConfidentialClientId = "fooddeliveryservice-confidential-client";
    private const string ConfidentialClientSecret = "PzotcrvZRF9BHCKcUxdKfHWlIPECG49k";

    private readonly PostgreSqlContainer _dbContainer = new PostgreSqlBuilder("postgres:17")
        .WithDatabase("fooddeliveryservice_orders")
        .WithUsername("postgres")
        .WithPassword("postgres")
        .Build();

    private readonly RedisContainer _redisContainer = new RedisBuilder("redis:latest")
        .Build();

    private readonly RabbitMqContainer _rabbitMqContainer = new RabbitMqBuilder("rabbitmq:management-alpine")
        .WithUsername("guest")
        .WithPassword("guest")
        .Build();

    private readonly List<Metric> _exportedMetrics = [];

    private readonly ExportCollection<Activity> _exportedActivities = [];

    private readonly CapturingLogSink _logSink = new();

    private UsersApiTestFactory? _usersApiFactory;

    /// <summary>
    /// The in-process Users.Api test host — used by the authorization tests to self-register a
    /// customer (so its real, non-manager permission set is returned by the RPC).
    /// </summary>
    internal UsersApiTestFactory UsersApi =>
        _usersApiFactory ?? throw new InvalidOperationException("The Users test host has not been initialized.");

    /// <summary>
    /// Email/password of the single Administrator test user seeded once for the whole run (real
    /// Identity credential + a real Users-module Administrator row) — reused by every test via
    /// <see cref="BaseIntegrationTest"/>. Administrator holds every Orders permission, including the
    /// admin-only <c>restaurants:create</c> that bypasses the ownership check on transitions.
    /// </summary>
    public string TestUserEmail { get; private set; } = string.Empty;

    public string TestUserPassword { get; } = "Orders-Tests-P@ssw0rd";

    /// <summary>
    /// The seeded Administrator's Users-module id — equal to the `sub` the Orders module resolves,
    /// so it is both the customer id for placement and the owner id for cancellation.
    /// </summary>
    public Guid TestUserId { get; private set; }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // Program.cs reads these eagerly in its top-level statements, before WebApplicationFactory's
        // deferred ConfigureAppConfiguration would apply — env vars are the only override visible in
        // time. Re-asserts Orders' own values in case the Users test host (built first, same env-var
        // keys) left its Postgres connection string behind.
        Environment.SetEnvironmentVariable("ConnectionStrings:Database", _dbContainer.GetConnectionString());
        Environment.SetEnvironmentVariable("ConnectionStrings:Cache", _redisContainer.GetConnectionString());
        Environment.SetEnvironmentVariable("ConnectionStrings:Queue", _rabbitMqContainer.GetConnectionString());

        // Reduce interval to 1 second to speed up outbox/inbox processing in tests.
        Environment.SetEnvironmentVariable("MessageProcessor:Outbox:IntervalInSeconds", "1");
        Environment.SetEnvironmentVariable("MessageProcessor:Inbox:IntervalInSeconds", "1");

        // appsettings points JWT Bearer's metadata address at the docker-internal Identity hostname,
        // unresolvable from a plain "dotnet test" process — point it at the localhost Identity is
        // reachable at here (ValidIssuers already accepts that issuer).
        Environment.SetEnvironmentVariable(
            "Authentication:MetadataAddress",
            $"{IdentityBaseUrl}/.well-known/openid-configuration");

        // Same story for the "Duende" readiness health check, which probes Identity's aggregate
        // /health at that same docker-internal hostname. Left alone it fails DNS and every run
        // reports the host unready — see the health probe tests.
        Environment.SetEnvironmentVariable("Duende:HealthUrl", $"{IdentityBaseUrl}/health");

        // A second metrics reader alongside the OTLP one AddInfrastructure wires up, so the metrics
        // tests can assert what this host actually exports without a collector. The smoke
        // diagnostics registration is the module-owned half of the same question: a meter declared
        // the way Delivery and Real-Time declare theirs, wired with the one call a host makes.
        builder.ConfigureServices(services =>
        {
            services.AddModuleDiagnostics(SmokeDiagnostics.Name);

            services.ConfigureOpenTelemetryMeterProvider(metrics =>
                metrics.AddInMemoryExporter(_exportedMetrics));

            // The trace equivalent: the spans this host produced, so the correlation tests can prove
            // a span that consumed a message off RabbitMQ is a child of the span that published it
            // in the Users host — the one assertion that fails if trace context stops crossing the
            // bus.
            services.ConfigureOpenTelemetryTracerProvider(tracing =>
                tracing.AddInMemoryExporter(_exportedActivities));

            // Serilog builds its logger once, inside the ILoggerFactory registration UseSerilog
            // adds, so there is no hook to attach a sink to a built host. Replacing the factory is
            // the hook: same LogContext enrichment as production (that is the thing under test),
            // Console/Seq swapped for an in-memory sink.
            services.AddSingleton<ILoggerFactory>(_ =>
            {
                Serilog.ILogger logger = new LoggerConfiguration()
                    .MinimumLevel.Information()
                    .Enrich.FromLogContext()
                    .WriteTo.Sink(_logSink)
                    .CreateLogger();

                var loggerFactory = new LoggerFactory();

                loggerFactory.AddProvider(new SerilogLoggerProvider(logger, dispose: true));

                return loggerFactory;
            });
        });
    }

    /// <summary>
    /// Every span the host has exported so far. Not cleared between calls: a cross-service
    /// assertion has to match a span here against one from the Users host, and the two are produced
    /// seconds apart by background jobs.
    /// </summary>
    public IReadOnlyList<Activity> CollectActivities()
    {
        Services.GetRequiredService<TracerProvider>().ForceFlush();

        return _exportedActivities.Snapshot();
    }

    /// <summary>
    /// The most recent log events the host wrote, with the properties the Serilog
    /// <c>LogContext</c> carried at the time — which is where the trace, span, service and business
    /// ids show up.
    /// </summary>
    public IReadOnlyList<LogEvent> CollectLogEvents() => _logSink.Snapshot();

    /// <summary>
    /// Collects everything the host's <see cref="MeterProvider"/> has aggregated since the last
    /// call and returns it. Metrics are exported on a periodic reader in production, so a test has
    /// to force the collection cycle itself.
    /// </summary>
    public IReadOnlyList<Metric> CollectMetrics()
    {
        _exportedMetrics.Clear();

        Services.GetRequiredService<MeterProvider>().ForceFlush();

        return [.. _exportedMetrics];
    }

    public async ValueTask InitializeAsync()
    {
        await _dbContainer.StartAsync();
        await _redisContainer.StartAsync();
        await _rabbitMqContainer.StartAsync();

        _usersApiFactory = new UsersApiTestFactory(
            _redisContainer.GetConnectionString(),
            _rabbitMqContainer.GetConnectionString());

        await _usersApiFactory.InitializeAsync();

        // Build the Users host eagerly (migrations applied, MassTransit endpoints bound) so the
        // permission RPC is answerable before the first authenticated request. Built strictly before
        // the Orders SUT host touches the shared env-var keys.
        _ = _usersApiFactory.Services;

        await SeedTestUserAsync();
    }

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        await _dbContainer.StopAsync();
        await _redisContainer.StopAsync();
        await _rabbitMqContainer.StopAsync();

        if (_usersApiFactory is not null)
        {
            await _usersApiFactory.DisposeAsync();
        }
    }

    /// <summary>
    /// Registers one Administrator test user, once per run: a real ASP.NET Identity credential
    /// against the locally running Identity service (docker-compose, not a testcontainer — must
    /// already be up) plus a matching Users-module Administrator row in the Users test host's own
    /// (ephemeral) database.
    /// </summary>
    private async Task SeedTestUserAsync()
    {
        // A unique email keeps registration idempotent-by-construction against Identity's real,
        // persistent store across repeated local runs.
        TestUserEmail = $"orders-tests+{Guid.NewGuid():N}@fooddeliveryservice.com";

        string identityId = await RegisterIdentityUserAsync(TestUserEmail, TestUserPassword);

        await using var scope = _usersApiFactory!.Services.CreateAsyncScope();

        var userRepository = scope.ServiceProvider.GetRequiredService<IUserRepository>();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        var user = User.Create(TestUserEmail, "Orders", "IntegrationTests", identityId, Role.Administrator);

        userRepository.Insert(user);

        await unitOfWork.SaveChangesAsync();

        TestUserId = user.Id;
    }

    private static async Task<string> RegisterIdentityUserAsync(string email, string password)
    {
        using var client = new HttpClient();

        // client_credentials token for the confidential client (users:register scope) — the same
        // mechanism DuendeAuthDelegatingHandler uses in production to call Identity's local API.
        var tokenRequestParameters = new KeyValuePair<string, string>[]
        {
            new("client_id", ConfidentialClientId),
            new("client_secret", ConfidentialClientSecret),
            new("grant_type", "client_credentials"),
            new("scope", "users:register")
        };

        using var tokenRequestContent = new FormUrlEncodedContent(tokenRequestParameters);

        using var tokenRequest = new HttpRequestMessage(HttpMethod.Post, new Uri($"{IdentityBaseUrl}/connect/token"))
        {
            Content = tokenRequestContent
        };

        using HttpResponseMessage tokenResponse = await client.SendAsync(tokenRequest);

        tokenResponse.EnsureSuccessStatusCode();

        ClientCredentialsToken clientCredentialsToken =
            (await tokenResponse.Content.ReadFromJsonAsync<ClientCredentialsToken>())!;

        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", clientCredentialsToken.AccessToken);

        using HttpResponseMessage registerResponse = await client.PostAsJsonAsync(
            $"{IdentityBaseUrl}/api/users",
            new { Email = email, FirstName = "Orders", LastName = "IntegrationTests", Password = password });

        registerResponse.EnsureSuccessStatusCode();

        RegisteredIdentityUser registeredUser =
            (await registerResponse.Content.ReadFromJsonAsync<RegisteredIdentityUser>())!;

        return registeredUser.Id;
    }

    private sealed class ClientCredentialsToken
    {
        [JsonPropertyName("access_token")]
        public string AccessToken { get; init; } = string.Empty;
    }

    private sealed class RegisteredIdentityUser
    {
        [JsonPropertyName("id")]
        public string Id { get; init; } = string.Empty;
    }
}

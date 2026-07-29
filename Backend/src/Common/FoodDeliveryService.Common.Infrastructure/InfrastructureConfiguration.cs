using Dapper;
using FoodDeliveryService.Common.Application.Caching;
using FoodDeliveryService.Common.Application.Clock;
using FoodDeliveryService.Common.Application.Data;
using FoodDeliveryService.Common.Application.EventBus;
using FoodDeliveryService.Common.Application.Locking;
using FoodDeliveryService.Common.Infrastructure.Authentication;
using FoodDeliveryService.Common.Infrastructure.Authorization;
using FoodDeliveryService.Common.Infrastructure.Caching;
using FoodDeliveryService.Common.Infrastructure.Clock;
using FoodDeliveryService.Common.Infrastructure.Data;
using FoodDeliveryService.Common.Infrastructure.EventBus;
using FoodDeliveryService.Common.Infrastructure.Locking;
using FoodDeliveryService.Common.Infrastructure.Outbox;
using MassTransit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Quartz;
using RabbitMQ.Client;
using StackExchange.Redis;

namespace FoodDeliveryService.Common.Infrastructure;

/// <summary>
/// Central infrastructure bootstrap shared by every API host. Each service's Program.cs calls
/// <see cref="AddInfrastructure(IServiceCollection, string, Action{IRegistrationConfigurator, string, string}[], RabbitMqSettings, string, string, bool)"/>
/// once, which wires up the whole cross-cutting stack: JWT authentication (tokens issued by Duende
/// IdentityServer), permission-based authorization, Npgsql + Dapper for read-side data access,
/// Quartz for the outbox/inbox background jobs, Redis for distributed caching, MassTransit over
/// RabbitMQ for messaging, and OpenTelemetry tracing exported to Jaeger.
/// <para>
/// The database-less overload
/// (<see cref="AddInfrastructure(IServiceCollection, string, Action{IRegistrationConfigurator, string, string}[], RabbitMqSettings, string, bool)"/>)
/// serves the one service that owns no PostgreSQL database — the Real-Time SignalR hub — by skipping
/// the Npgsql/Dapper/outbox-interceptor registrations while keeping everything else identical.
/// </para>
/// </summary>
public static class InfrastructureConfiguration
{
    /// <summary>
    /// Full stack for a database-backed service: everything in the database-less overload PLUS the
    /// Npgsql data source, the Dapper connection factory (CQRS read side) and the transactional
    /// outbox interceptor.
    /// </summary>
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        string serviceName,
        Action<IRegistrationConfigurator, string, string>[] moduleConfigureConsumers,
        RabbitMqSettings rabbitMqSettings,
        string databaseConnectionString,
        string redisConnectionString,
        bool allowInMemoryCacheFallback = true)
    {
        // Npgsql: the PostgreSQL ADO.NET driver. A single pooled NpgsqlDataSource per service is
        // shared by Dapper queries and the outbox/inbox jobs; EF Core opens its own connections
        // from the same connection string (one database per service).
        NpgsqlDataSource npgsqlDataSource = new NpgsqlDataSourceBuilder(databaseConnectionString).Build();
        services.TryAddSingleton(npgsqlDataSource);

        // Dapper entry point: query handlers (CQRS read side) inject IDbConnectionFactory and run
        // raw SQL with Dapper — EF Core is reserved for the write side (commands + repositories).
        services.TryAddScoped<IDbConnectionFactory, DbConnectionFactory>();

        // Dapper type handler that maps PostgreSQL arrays (e.g. text[]) to .NET arrays.
        SqlMapper.AddTypeHandler(new GenericArrayHandler<string>());

        // EF Core SaveChanges interceptor that converts raised domain events into outbox_messages
        // rows inside the same transaction as the business change (transactional outbox pattern).
        services.TryAddSingleton<InsertOutboxMessagesInterceptor>();

        // Everything else (auth, authorization, messaging, cache, tracing) is identical to the
        // database-less host, so share the one implementation.
        return services.AddInfrastructure(
            serviceName,
            moduleConfigureConsumers,
            rabbitMqSettings,
            redisConnectionString,
            allowInMemoryCacheFallback);
    }

    /// <summary>
    /// Database-less variant for a service that owns no PostgreSQL database — the Real-Time SignalR
    /// hub (<c>fooddeliveryservice.realtime.api</c>). It fans out ephemeral socket frames over a
    /// Redis backplane and consumes lifecycle events with direct bus consumers; it has no
    /// aggregates, no outbox/inbox and therefore no Npgsql/Dapper or outbox interceptor. All the
    /// remaining cross-cutting wiring — JWT auth, permission authorization, Quartz, Redis cache,
    /// MassTransit/RabbitMQ and OpenTelemetry — is exactly as the database-backed hosts get it.
    /// </summary>
    /// <param name="allowInMemoryCacheFallback">
    /// Whether an unreachable Redis may be replaced by an in-process cache and an in-process lock.
    /// Hosts pass <c>builder.Environment.IsDevelopment()</c>: convenient for a local run without the
    /// container, wrong everywhere else, because a process-local <c>IDistributedLock</c> silently
    /// stops protecting anything as soon as a second replica exists.
    /// </param>
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        string serviceName,
        Action<IRegistrationConfigurator, string, string>[] moduleConfigureConsumers,
        RabbitMqSettings rabbitMqSettings,
        string redisConnectionString,
        bool allowInMemoryCacheFallback = true)
    {
        // ASP.NET Core JWT Bearer authentication. Tokens are issued by the Duende IdentityServer
        // host (:18080); each service validates them independently against Duende's OpenID Connect
        // discovery endpoint (bound from the "Authentication" section via JwtBearerConfigureOptions).
        services.AddAuthenticationInternal();

        // Permission-based authorization: CustomClaimsTransformation enriches the JWT principal
        // with permissions fetched from the Users service (see IPermissionService), and
        // PermissionAuthorizationPolicyProvider turns .RequireAuthorization("permission:x")
        // into a policy checked by PermissionAuthorizationHandler.
        services.AddAuthorizationInternal();

        services.TryAddSingleton<IDateTimeProvider, DateTimeProvider>();

        // IEventBus is the ONLY way services publish integration events; it delegates to
        // MassTransit's IBus so traces and message topology stay consistent.
        services.TryAddSingleton<IEventBus, EventBus.EventBus>();

        // Quartz: in-process job scheduler that drives the messaging background jobs —
        // ProcessOutboxJob (dispatch domain events, publish integration events) and
        // ProcessInboxJob (dispatch consumed integration events). Each module registers its own
        // jobs via ConfigureProcessOutboxJob/ConfigureProcessInboxJob in its {Module}Module class.
        services.AddQuartz(configurator =>
        {
            var scheduler = Guid.NewGuid();
            configurator.SchedulerId = $"default-id-{scheduler}";
            configurator.SchedulerName = $"default-name-{scheduler}";
        });

        services.AddQuartzHostedService(options => options.WaitForJobsToComplete = true);

        // StackExchange.Redis: distributed cache used by ICacheService — most importantly to cache
        // user permissions for 5 minutes so authorization doesn't hit the Users service on every
        // request. RedisConnectionOptions hardens the parsed connection string (abortConnect=false +
        // exponential reconnect; TLS and everything else stay the connection string's business),
        // which is what lets the same code path serve a local container and Azure Cache for Redis
        // with only configuration changing. See docs/caching.md.
        //
        // Because abortConnect is false, Connect() returns a usable, self-reconnecting multiplexer
        // even against a server that is currently down — it throws only when the connection string
        // itself is unusable, which is a misconfiguration worth failing fast on. The multiplexer is
        // therefore ALWAYS registered: Delivery's driver-location GEO store and Real-Time's
        // location subscriber depend on it directly, and a Redis outage should degrade those calls,
        // not fail their DI resolution.
        var connectionMultiplexer =
            ConnectionMultiplexer.Connect(RedisConnectionOptions.Create(redisConnectionString, serviceName));

        services.AddSingleton<IConnectionMultiplexer>(connectionMultiplexer);

        if (connectionMultiplexer.IsConnected || !allowInMemoryCacheFallback)
        {
            services.AddStackExchangeRedisCache(options =>
                options.ConnectionMultiplexerFactory = () => Task.FromResult<IConnectionMultiplexer>(connectionMultiplexer));

            // Cross-instance mutual exclusion on the same connection (SET NX PX + owner-checked
            // release). Used by Delivery's driver assignment, where two overlapping triggers could
            // otherwise offer one delivery twice or hand one driver two orders.
            services.TryAddSingleton<IDistributedLock, RedisDistributedLock>();
        }
        else
        {
            // Keep a Redis-less local run bootable. Never reached when the caller refuses the
            // fallback: there the host boots against the reconnecting multiplexer instead and its
            // Redis health check reports unhealthy until the server is back — degrading a replica to
            // a process-local cache and a process-local lock is worse than being out of rotation.
            services.AddDistributedMemoryCache();

            // Same reason as the in-memory cache above. It only excludes callers inside this
            // process — see InMemoryDistributedLock — so the fallback is announced at startup.
            services.TryAddSingleton<IDistributedLock, InMemoryDistributedLock>();

            services.AddHostedService<InMemoryFallbackWarning>();
        }

        services.ConfigureOptions<CachingConfigureOptions>();

        services.TryAddSingleton<ICacheService, CacheService>();

        // Raw RabbitMQ.Client connection — NOT used for messaging, which MassTransit owns.
        // It exists only so the RabbitMQ health check in each host's Program.cs can ping the broker.
        services.AddSingleton<IConnection>(sp =>
        {
            var factory = new ConnectionFactory
            {
                Uri = new Uri(rabbitMqSettings.Host)
            };

            return factory.CreateConnectionAsync().GetAwaiter().GetResult();
        });

        // MassTransit: the messaging framework on top of RabbitMQ. It handles publish/subscribe of
        // integration events, request/response (IRequestClient — e.g. GetUserPermissionsRequest to
        // the Users service), serialization, retries and queue topology.
        services.AddMassTransit(configure =>
        {
            // Each module contributes its consumers here. The per-service instanceId suffixes the
            // queue names so every service gets its OWN queue (true pub/sub fan-out) — without it,
            // services would share one queue and compete for messages.
            string instanceId = serviceName.ToLowerInvariant().Replace('.', '-'); // FoodDeliveryService.Users.Api -> fooddeliveryservice-users-api
            foreach (Action<IRegistrationConfigurator, string, string> configureConsumers in moduleConfigureConsumers)
            {
                configureConsumers(configure, instanceId, redisConnectionString);
            }

            // Queue/exchange names derived from consumer names in kebab-case.
            configure.SetKebabCaseEndpointNameFormatter();

            configure.UsingRabbitMq((context, cfg) =>
            {
                cfg.Host(new Uri(rabbitMqSettings.Host), h =>
                {
                    h.Username(rabbitMqSettings.Username);
                    h.Password(rabbitMqSettings.Password);
                });
                // Auto-creates receive endpoints (queues) for all registered consumers.
                cfg.ConfigureEndpoints(context);
            });
        });

        // OpenTelemetry distributed tracing. Every incoming request, outgoing HTTP call, EF Core /
        // Npgsql query, Redis command and MassTransit message (traces propagate across RabbitMQ)
        // becomes a span. The OTLP exporter ships spans to Jaeger (endpoint from the standard
        // OTEL_EXPORTER_OTLP_ENDPOINT setting, :4317; browse traces at :16686). serviceName is what
        // shows up in the Jaeger service dropdown.
        services
            .AddOpenTelemetry()
            .ConfigureResource(resource => resource.AddService(serviceName))
            .WithTracing(tracing =>
            {
                tracing
                    .AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation()
                    .AddEntityFrameworkCoreInstrumentation()
                    .AddRedisInstrumentation()
                    .AddNpgsql()
                    .AddSource(MassTransit.Logging.DiagnosticHeaders.DefaultListenerName);

                tracing.AddOtlpExporter();
            });

        return services;
    }
}

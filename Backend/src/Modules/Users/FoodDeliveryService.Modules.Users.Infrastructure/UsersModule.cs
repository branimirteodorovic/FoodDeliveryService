using FoodDeliveryService.Common.Application.Authorization;
using FoodDeliveryService.Common.Application.EventBus;
using FoodDeliveryService.Common.Application.Messaging;
using FoodDeliveryService.Common.Infrastructure.Outbox;
using FoodDeliveryService.Common.Presentation.Endpoints;
using FoodDeliveryService.Modules.Users.Application.Abstractions.Data;
using FoodDeliveryService.Modules.Users.Application.Abstractions.Identity;
using FoodDeliveryService.Modules.Users.Domain.Users;
using FoodDeliveryService.Modules.Users.Infrastructure.Authorization;
using FoodDeliveryService.Modules.Users.Infrastructure.Database;
using FoodDeliveryService.Modules.Users.Infrastructure.Identity;
using FoodDeliveryService.Modules.Users.Infrastructure.Inbox;
using FoodDeliveryService.Modules.Users.Infrastructure.Outbox;
using FoodDeliveryService.Modules.Users.Infrastructure.Users;
using FoodDeliveryService.Modules.Users.Presentation.Users;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace FoodDeliveryService.Modules.Users.Infrastructure;

/// <summary>
/// Composition root of the Users module — everything the module needs beyond the shared
/// AddInfrastructure stack is registered here: its EF Core DbContext, repositories, event
/// handlers, endpoints, the Duende provisioning HTTP client and the outbox/inbox Quartz jobs.
/// This is the reference implementation other modules follow.
/// </summary>
public static class UsersModule
{
    public static IServiceCollection AddUsersModule(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddDomainEventHandlers();

        services.AddIntegrationEventHandlers();

        services.AddInfrastructure(configuration);

        services.AddEndpoints(Presentation.AssemblyReference.Assembly);

        return services;
    }

    /// <summary>
    /// MassTransit consumers this module brings to its host, invoked from AddInfrastructure's
    /// AddMassTransit call. The Users module consumes no integration events; it only serves the
    /// GetUserPermissionsRequest request/response used by other services for authorization.
    /// The instanceId suffix gives the host its own queue name.
    /// </summary>
    public static Action<IRegistrationConfigurator, string, string> ConfigureConsumers()
    {
        return (registration, instanceId, redisConnectionString) =>
        {
            registration.AddConsumer<GetUserPermissionsRequestConsumer>()
            .Endpoint(c => c.InstanceId = instanceId);
        };
    }

    private static void AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        // In the Users service permissions are read straight from its own database — the other
        // services' PermissionService implementations reach this data via MassTransit
        // request/response instead.
        services.AddScoped<IPermissionService, PermissionService>();

        // Typed HttpClient for Duende IdentityServer's local API (api/users). The delegating
        // handler transparently obtains a client-credentials token (scope users:register) and
        // attaches it as a Bearer header. This is the only sanctioned service-to-service HTTP
        // call in the system — used to provision credentials during user registration.
        services.Configure<DuendeOptions>(configuration.GetSection("Duende"));

        services.AddTransient<DuendeAuthDelegatingHandler>();

        services
            .AddHttpClient<DuendeIdentityClient>((serviceProvider, httpClient) =>
            {
                DuendeOptions duendeOptions = serviceProvider
                    .GetRequiredService<IOptions<DuendeOptions>>().Value;

                httpClient.BaseAddress = new Uri(duendeOptions.AdminUrl);
            })
            .AddHttpMessageHandler<DuendeAuthDelegatingHandler>();

        services.AddTransient<IIdentityProviderService, IdentityProviderService>();

        // EF Core (write side only — queries use Dapper): PostgreSQL via Npgsql, snake_case
        // table/column names, plus the outbox interceptor that persists raised domain events in
        // the same transaction as the business change.
        services.AddDbContext<UsersDbContext>((sp, options) =>
            options
                .UseNpgsql(
                    configuration.GetConnectionString("Database"),
                    npgsqlOptions => npgsqlOptions
                        .MigrationsHistoryTable(HistoryRepository.DefaultTableName))
                .AddInterceptors(sp.GetRequiredService<InsertOutboxMessagesInterceptor>())
                .UseSnakeCaseNamingConvention());

        services.AddScoped<IUserRepository, UserRepository>();

        // IUnitOfWork is the DbContext itself; handlers commit through it, never SaveChanges directly.
        services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<UsersDbContext>());

        // Schedules the module's Quartz jobs (interval/batch size from "MessageProcessor" config):
        // ProcessOutboxJob dispatches stored domain events, ProcessInboxJob dispatches stored
        // integration events.
        services.Configure<OutboxOptions>(configuration.GetSection("MessageProcessor:Outbox"));

        services.ConfigureOptions<ConfigureProcessOutboxJob>();

        services.Configure<InboxOptions>(configuration.GetSection("MessageProcessor:Inbox"));

        services.ConfigureOptions<ConfigureProcessInboxJob>();
    }

    private static void AddDomainEventHandlers(this IServiceCollection services)
    {
        Type[] domainEventHandlers = Application.AssemblyReference.Assembly
            .GetTypes()
            .Where(t => t.IsAssignableTo(typeof(IDomainEventHandler)))
            .ToArray();

        foreach (Type domainEventHandler in domainEventHandlers)
        {
            services.TryAddScoped(domainEventHandler);

            Type domainEvent = domainEventHandler
                .GetInterfaces()
                .Single(i => i.IsGenericType)
                .GetGenericArguments()
                .Single();

            Type closedIdempotentHandler = typeof(IdempotentDomainEventHandler<>).MakeGenericType(domainEvent);

            services.Decorate(domainEventHandler, closedIdempotentHandler);
        }
    }

    private static void AddIntegrationEventHandlers(this IServiceCollection services)
    {
        Type[] integrationEventHandlers = Presentation.AssemblyReference.Assembly
            .GetTypes()
            .Where(t => t.IsAssignableTo(typeof(IIntegrationEventHandler)))
            .ToArray();

        foreach (Type integrationEventHandler in integrationEventHandlers)
        {
            services.TryAddScoped(integrationEventHandler);

            Type integrationEvent = integrationEventHandler
                .GetInterfaces()
                .Single(i => i.IsGenericType)
                .GetGenericArguments()
                .Single();

            Type closedIdempotentHandler =
                typeof(IdempotentIntegrationEventHandler<>).MakeGenericType(integrationEvent);

            services.Decorate(integrationEventHandler, closedIdempotentHandler);
        }
    }
}

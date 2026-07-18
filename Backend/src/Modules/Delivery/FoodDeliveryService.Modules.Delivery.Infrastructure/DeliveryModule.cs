using FoodDeliveryService.Common.Application.Authorization;
using FoodDeliveryService.Common.Application.EventBus;
using FoodDeliveryService.Common.Application.Messaging;
using FoodDeliveryService.Common.Infrastructure.Outbox;
using FoodDeliveryService.Common.Presentation.Endpoints;
using FoodDeliveryService.Modules.Delivery.Application.Abstractions.Authentication;
using FoodDeliveryService.Modules.Delivery.Application.Abstractions.Data;
using FoodDeliveryService.Modules.Delivery.Application.Abstractions.Provisioning;
using FoodDeliveryService.Modules.Delivery.Domain.Drivers;
using FoodDeliveryService.Modules.Delivery.Infrastructure.Authentication;
using FoodDeliveryService.Modules.Delivery.Infrastructure.Authorization;
using FoodDeliveryService.Modules.Delivery.Infrastructure.Database;
using FoodDeliveryService.Modules.Delivery.Infrastructure.Drivers;
using FoodDeliveryService.Modules.Delivery.Application.Abstractions.Locations;
using FoodDeliveryService.Modules.Delivery.Infrastructure.Inbox;
using FoodDeliveryService.Modules.Delivery.Infrastructure.Locations;
using FoodDeliveryService.Modules.Delivery.Infrastructure.Outbox;
using FoodDeliveryService.Modules.Delivery.Infrastructure.Provisioning;
using FoodDeliveryService.Modules.Users.IntegrationEvents;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace FoodDeliveryService.Modules.Delivery.Infrastructure;

public static class DeliveryModule
{
    public static IServiceCollection AddDeliveryModule(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddDomainEventHandlers();

        services.AddIntegrationEventHandlers();

        services.AddInfrastructure(configuration);

        services.AddEndpoints(Presentation.AssemblyReference.Assembly);

        return services;
    }

    public static Action<IRegistrationConfigurator, string, string> ConfigureConsumers()
    {
        return (registration, instanceId, _) =>
        {
            // Keeps the Driver's name snapshot in sync with Users. No UserRegistered consumer —
            // drivers only come into existence through the provisioning RPC below, never from a
            // registration event.
            registration.AddConsumer<IntegrationEventConsumer<UserProfileUpdatedIntegrationEvent>>()
                .Endpoint(c => c.InstanceId = instanceId);

            // Explicit request clients for the RPCs this module sends to Users (see
            // Authorization/PermissionService.cs and Provisioning/DriverProvisioningService.cs) —
            // without these, MassTransit's implicit IRequestClient<T> resolution silently fails to
            // route the request and every call times out.
            registration.AddRequestClient<GetUserPermissionsRequest>();
            registration.AddRequestClient<ProvisionUserRequest>();
            registration.AddRequestClient<DeactivateProvisionedUserRequest>();
        };
    }

    private static void AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<DeliveryDbContext>((sp, options) =>
            options
                .UseNpgsql(
                    configuration.GetConnectionString("Database"),
                    npgsqlOptions => npgsqlOptions
                        .MigrationsHistoryTable(HistoryRepository.DefaultTableName))
                .UseSnakeCaseNamingConvention()
                .AddInterceptors(sp.GetRequiredService<InsertOutboxMessagesInterceptor>()));

        services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<DeliveryDbContext>());

        services.AddScoped<IDriversRepository, DriversRepository>();

        services.AddScoped<IDeliveryContext, DeliveryContext>();

        // Live driver positions: Redis GEO for the "nearest available" search + a TTL'd position
        // hash for freshness, with history appended to Postgres. Swappable for Cosmos (Milestone G)
        // behind the same interface. Scoped — it writes history through the request's DbContext.
        services.Configure<DriverLocationStoreOptions>(configuration.GetSection("Delivery:LocationStore"));

        services.AddScoped<IDriverLocationStore, RedisDriverLocationStore>();

        // Synchronous onboarding calls to Users (MassTransit request/response).
        services.AddScoped<IDriverProvisioningService, DriverProvisioningService>();

        services.AddScoped<IPermissionService, PermissionService>();

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

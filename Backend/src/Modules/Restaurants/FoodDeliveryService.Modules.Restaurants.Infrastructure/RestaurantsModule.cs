using FoodDeliveryService.Common.Application.EventBus;
using FoodDeliveryService.Common.Application.Messaging;
using FoodDeliveryService.Common.Infrastructure.Outbox;
using FoodDeliveryService.Common.Presentation.Endpoints;
using FoodDeliveryService.Modules.Restaurants.Application.Abstractions.Authentication;
using FoodDeliveryService.Modules.Restaurants.Application.Abstractions.Data;
using FoodDeliveryService.Modules.Restaurants.Application.Abstractions.Provisioning;
using FoodDeliveryService.Modules.Restaurants.Domain.Managers;
using FoodDeliveryService.Modules.Restaurants.Domain.Restaurants;
using FoodDeliveryService.Modules.Restaurants.Infrastructure.Authentication;
using FoodDeliveryService.Modules.Restaurants.Infrastructure.Database;
using FoodDeliveryService.Modules.Restaurants.Infrastructure.Inbox;
using FoodDeliveryService.Modules.Restaurants.Infrastructure.Managers;
using FoodDeliveryService.Modules.Restaurants.Infrastructure.Outbox;
using FoodDeliveryService.Modules.Restaurants.Infrastructure.Provisioning;
using FoodDeliveryService.Modules.Restaurants.Infrastructure.Restaurants;
using FoodDeliveryService.Modules.Users.IntegrationEvents;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace FoodDeliveryService.Modules.Restaurants.Infrastructure;

public static class RestaurantsModule
{
    public static IServiceCollection AddRestaurantsModule(
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
        return (registration, instanceId, redisConnectionString) =>
        {
            registration.AddConsumer<IntegrationEventConsumer<UserRegisteredIntegrationEvent>>()
            .Endpoint(c => c.InstanceId = instanceId);
            registration.AddConsumer<IntegrationEventConsumer<UserProfileUpdatedIntegrationEvent>>()
                .Endpoint(c => c.InstanceId = instanceId);
        };
    }

    private static void AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<RestaurantsDbContext>((sp, options) =>
            options
                .UseNpgsql(
                    configuration.GetConnectionString("Database"),
                    npgsqlOptions => npgsqlOptions
                        .MigrationsHistoryTable(HistoryRepository.DefaultTableName))
                .UseSnakeCaseNamingConvention()
                .AddInterceptors(sp.GetRequiredService<InsertOutboxMessagesInterceptor>()));

        services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<RestaurantsDbContext>());

        services.AddScoped<IRestaurantsRepository, RestaurantsRepository>();

        services.AddScoped<IRestaurantManagersRepository, RestaurantManagersRepository>();

        services.AddScoped<IRestaurantsContext, RestaurantsContext>();

        // Synchronous onboarding calls to Users (MassTransit request/response — IRequestClient<T>
        // is resolved by MassTransit's DI integration, no explicit registration needed).
        services.AddScoped<IManagerProvisioningService, ManagerProvisioningService>();

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

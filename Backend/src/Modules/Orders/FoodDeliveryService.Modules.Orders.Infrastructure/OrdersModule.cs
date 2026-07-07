using FoodDeliveryService.Common.Application.Authorization;
using FoodDeliveryService.Common.Application.EventBus;
using FoodDeliveryService.Common.Application.Messaging;
using FoodDeliveryService.Common.Infrastructure.Outbox;
using FoodDeliveryService.Common.Presentation.Endpoints;
using FoodDeliveryService.Modules.Orders.Application.Abstractions.Authentication;
using FoodDeliveryService.Modules.Orders.Application.Abstractions.Data;
using FoodDeliveryService.Modules.Orders.Domain.Customers;
using FoodDeliveryService.Modules.Orders.Domain.Orders;
using FoodDeliveryService.Modules.Orders.Domain.Restaurants;
using FoodDeliveryService.Modules.Orders.Infrastructure.Authentication;
using FoodDeliveryService.Modules.Orders.Infrastructure.Authorization;
using FoodDeliveryService.Modules.Orders.Infrastructure.Customers;
using FoodDeliveryService.Modules.Orders.Infrastructure.Database;
using FoodDeliveryService.Modules.Orders.Infrastructure.Inbox;
using FoodDeliveryService.Modules.Orders.Infrastructure.Orders;
using FoodDeliveryService.Modules.Orders.Infrastructure.Outbox;
using FoodDeliveryService.Modules.Orders.Infrastructure.Restaurants;
using FoodDeliveryService.Modules.Restaurants.IntegrationEvents;
using FoodDeliveryService.Modules.Users.IntegrationEvents;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace FoodDeliveryService.Modules.Orders.Infrastructure;

public static class OrdersModule
{
    public static IServiceCollection AddOrdersModule(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddDomainEventHandlers();

        services.AddIntegrationEventHandlers();

        services.AddInfrastructure(configuration);

        services.AddEndpoints(Presentation.AssemblyReference.Assembly);

        return services;
    }

#pragma warning disable IDE0060 // Remove unused parameter
    public static void ConfigureConsumers(IRegistrationConfigurator registrationConfigurator, string instanceId, string redisConnectionString)
#pragma warning restore IDE0060 // Remove unused parameter
#pragma warning disable S125 // Sections of code should not be commented out
    {
        registrationConfigurator.AddConsumer<IntegrationEventConsumer<UserRegisteredIntegrationEvent>>()
            .Endpoint(c => c.InstanceId = instanceId);
        registrationConfigurator.AddConsumer<IntegrationEventConsumer<UserProfileUpdatedIntegrationEvent>>()
            .Endpoint(c => c.InstanceId = instanceId);

        registrationConfigurator.AddConsumer<IntegrationEventConsumer<RestaurantRegisteredIntegrationEvent>>()
            .Endpoint(c => c.InstanceId = instanceId);
        registrationConfigurator.AddConsumer<IntegrationEventConsumer<MenuItemAddedIntegrationEvent>>()
            .Endpoint(c => c.InstanceId = instanceId);
        registrationConfigurator.AddConsumer<IntegrationEventConsumer<MenuItemUpdatedIntegrationEvent>>()
            .Endpoint(c => c.InstanceId = instanceId);
        registrationConfigurator.AddConsumer<IntegrationEventConsumer<MenuItemAvailabilityChangedIntegrationEvent>>()
            .Endpoint(c => c.InstanceId = instanceId);

        //registrationConfigurator
        //    .AddSagaStateMachine<CancelEventSaga, CancelEventState>()
        //    .RedisRepository(redisConnectionString);
    }
#pragma warning restore S125 // Sections of code should not be commented out

    private static void AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<OrdersDbContext>((sp, options) =>
            options
                .UseNpgsql(
                    configuration.GetConnectionString("Database"),
                    npgsqlOptions => npgsqlOptions
                        .MigrationsHistoryTable(HistoryRepository.DefaultTableName))
                .UseSnakeCaseNamingConvention()
                .AddInterceptors(sp.GetRequiredService<InsertOutboxMessagesInterceptor>()));

        services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<OrdersDbContext>());

        services.AddScoped<IOrdersRepository, OrdersRepository>();

        services.AddScoped<ICustomerRepository, CustomersRepository>();

        services.AddScoped<IRestaurantReplicaRepository, RestaurantReplicaRepository>();

        services.AddScoped<IMenuItemReplicaRepository, MenuItemReplicaRepository>();

        services.AddScoped<IOrdersContext, OrdersContext>();

        services.Configure<OutboxOptions>(configuration.GetSection("MessageProcessor:Outbox"));

        services.ConfigureOptions<ConfigureProcessOutboxJob>();

        services.Configure<InboxOptions>(configuration.GetSection("MessageProcessor:Inbox"));

        services.ConfigureOptions<ConfigureProcessInboxJob>();

        services.AddScoped<IPermissionService, PermissionService>();
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

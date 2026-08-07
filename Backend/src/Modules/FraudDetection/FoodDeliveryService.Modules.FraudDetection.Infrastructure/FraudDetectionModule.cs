using FoodDeliveryService.Common.Application.Authorization;
using FoodDeliveryService.Common.Application.EventBus;
using FoodDeliveryService.Common.Application.Messaging;
using FoodDeliveryService.Common.Infrastructure.Outbox;
using FoodDeliveryService.Common.Presentation.Endpoints;
using FoodDeliveryService.Modules.Delivery.IntegrationEvents;
using FoodDeliveryService.Modules.FraudDetection.Application.Abstractions.Data;
using FoodDeliveryService.Modules.FraudDetection.Domain.Customers;
using FoodDeliveryService.Modules.FraudDetection.Domain.Drivers;
using FoodDeliveryService.Modules.FraudDetection.Domain.Orders;
using FoodDeliveryService.Modules.FraudDetection.Infrastructure.Authorization;
using FoodDeliveryService.Modules.FraudDetection.Infrastructure.Customers;
using FoodDeliveryService.Modules.FraudDetection.Infrastructure.Database;
using FoodDeliveryService.Modules.FraudDetection.Infrastructure.Drivers;
using FoodDeliveryService.Modules.FraudDetection.Infrastructure.Inbox;
using FoodDeliveryService.Modules.FraudDetection.Infrastructure.Orders;
using FoodDeliveryService.Modules.FraudDetection.Infrastructure.Outbox;
using FoodDeliveryService.Modules.Orders.IntegrationEvents;
using FoodDeliveryService.Modules.Users.IntegrationEvents;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace FoodDeliveryService.Modules.FraudDetection.Infrastructure;

public static class FraudDetectionModule
{
    public static IServiceCollection AddFraudDetectionModule(
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
    /// FraudDetection's consumer set is unusually wide and entirely one-directional: it subscribes to the
    /// order, delivery and account lifecycles of three other services and publishes nothing back in
    /// this milestone. Each consumer only writes the message to <c>inbox_messages</c>; the
    /// projection updates happen later, on ProcessInboxJob's thread, so nothing here sits on the
    /// latency path of the request that produced the event.
    /// </summary>
    public static Action<IRegistrationConfigurator, string, string> ConfigureConsumers()
    {
        return (registration, instanceId, _) =>
        {
            // Account age — the denominator of every "new account" signal (Milestone E).
            registration.AddConsumer<IntegrationEventConsumer<UserRegisteredIntegrationEvent>>()
                .Endpoint(c => c.InstanceId = instanceId);

            // The order lifecycle, from Orders.
            registration.AddConsumer<IntegrationEventConsumer<OrderPlacedIntegrationEvent>>()
                .Endpoint(c => c.InstanceId = instanceId);

            registration.AddConsumer<IntegrationEventConsumer<OrderAcceptedIntegrationEvent>>()
                .Endpoint(c => c.InstanceId = instanceId);

            // Not in the plan's list for this milestone, and consumed anyway: it is the only shipped
            // event carrying the delivery coordinates the fact table needs (see
            // RecordOrderReadyForPickupCommand).
            registration.AddConsumer<IntegrationEventConsumer<OrderReadyForPickupIntegrationEvent>>()
                .Endpoint(c => c.InstanceId = instanceId);

            registration.AddConsumer<IntegrationEventConsumer<OrderCancelledIntegrationEvent>>()
                .Endpoint(c => c.InstanceId = instanceId);

            registration.AddConsumer<IntegrationEventConsumer<OrderRejectedIntegrationEvent>>()
                .Endpoint(c => c.InstanceId = instanceId);

            // The delivery lifecycle, from Delivery.
            registration.AddConsumer<IntegrationEventConsumer<OrderPickedUpIntegrationEvent>>()
                .Endpoint(c => c.InstanceId = instanceId);

            registration.AddConsumer<IntegrationEventConsumer<OrderDeliveredIntegrationEvent>>()
                .Endpoint(c => c.InstanceId = instanceId);

            registration.AddConsumer<IntegrationEventConsumer<DeliveryOfferRejectedIntegrationEvent>>()
                .Endpoint(c => c.InstanceId = instanceId);

            registration.AddConsumer<IntegrationEventConsumer<DeliveryUnassignedIntegrationEvent>>()
                .Endpoint(c => c.InstanceId = instanceId);

            // The one RPC this module sends: permission resolution for the triage endpoints that
            // arrive in Milestone C. Registered now because CustomClaimsTransformation resolves
            // IPermissionService on every authenticated request, and without the explicit request
            // client MassTransit fails to route it and every call times out.
            registration.AddRequestClient<GetUserPermissionsRequest>();
        };
    }

    private static void AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<FraudDetectionDbContext>((sp, options) =>
            options
                .UseNpgsql(
                    configuration.GetConnectionString("Database"),
                    npgsqlOptions => npgsqlOptions
                        .MigrationsHistoryTable(HistoryRepository.DefaultTableName))
                .UseSnakeCaseNamingConvention()
                .AddInterceptors(sp.GetRequiredService<InsertOutboxMessagesInterceptor>()));

        services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<FraudDetectionDbContext>());

        // The three behavioural projections. All write-side only in this milestone — there is no
        // read API until Milestone C, and when there is, it is Dapper (hard rule #2).
        services.AddScoped<ICustomerBehavioursRepository, CustomerBehavioursRepository>();

        services.AddScoped<IDriverBehavioursRepository, DriverBehavioursRepository>();

        services.AddScoped<IOrderFactsRepository, OrderFactsRepository>();

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

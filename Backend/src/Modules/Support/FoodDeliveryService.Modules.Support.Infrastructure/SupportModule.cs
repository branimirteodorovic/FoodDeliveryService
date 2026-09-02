using FoodDeliveryService.Common.Application.Authorization;
using FoodDeliveryService.Common.Application.EventBus;
using FoodDeliveryService.Common.Application.Messaging;
using FoodDeliveryService.Common.Infrastructure.Outbox;
using FoodDeliveryService.Common.Presentation.Endpoints;
using FoodDeliveryService.Modules.Support.Application.Abstractions.Audit;
using FoodDeliveryService.Modules.Support.Application.Abstractions.Authentication;
using FoodDeliveryService.Modules.Support.Application.Abstractions.Data;
using FoodDeliveryService.Modules.Support.Domain.Agents;
using FoodDeliveryService.Modules.Support.Domain.Audit;
using FoodDeliveryService.Modules.Support.Domain.Orders;
using FoodDeliveryService.Modules.Support.Domain.Refunds;
using FoodDeliveryService.Modules.Support.Domain.Tickets;
using FoodDeliveryService.Modules.Support.Infrastructure.Agents;
using FoodDeliveryService.Modules.Support.Infrastructure.Audit;
using FoodDeliveryService.Modules.Support.Infrastructure.Authentication;
using FoodDeliveryService.Modules.Support.Infrastructure.Authorization;
using FoodDeliveryService.Modules.Support.Infrastructure.Database;
using FoodDeliveryService.Modules.Support.Infrastructure.Inbox;
using FoodDeliveryService.Modules.Support.Infrastructure.Orders;
using FoodDeliveryService.Modules.Support.Infrastructure.Outbox;
using FoodDeliveryService.Modules.Support.Infrastructure.Refunds;
using FoodDeliveryService.Modules.Support.Infrastructure.Tickets;
using FoodDeliveryService.Modules.Orders.IntegrationEvents;
using FoodDeliveryService.Modules.Users.IntegrationEvents;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace FoodDeliveryService.Modules.Support.Infrastructure;

public static class SupportModule
{
    public static IServiceCollection AddSupportModule(
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
            // The agent replica: who a ticket can be assigned to, and whose name a ticket list
            // renders. Both handlers skip users that are not support staff. The customer replica
            // arrives in the context milestone as a further subscription here.
            registration.AddConsumer<IntegrationEventConsumer<UserRegisteredIntegrationEvent>>()
                .Endpoint(c => c.InstanceId = instanceId);
            registration.AddConsumer<IntegrationEventConsumer<UserProfileUpdatedIntegrationEvent>>()
                .Endpoint(c => c.InstanceId = instanceId);

            // The order replica. Only the placed event today, because the refund ceiling is the
            // only fact about an order this service currently needs and that is the event carrying
            // the subtotal. The other seven lifecycle events join it in the ticket-context
            // milestone, each as one more subscription here alongside its own handler.
            registration.AddConsumer<IntegrationEventConsumer<OrderPlacedIntegrationEvent>>()
                .Endpoint(c => c.InstanceId = instanceId);

            // The explicit request client is not optional: without it MassTransit's implicit
            // IRequestClient<T> resolution silently fails to route the request and every
            // permission lookup times out, which surfaces as a blanket 403 rather than as an error.
            registration.AddRequestClient<GetUserPermissionsRequest>();
        };
    }

    private static void AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<SupportDbContext>((sp, options) =>
            options
                .UseNpgsql(
                    configuration.GetConnectionString("Database"),
                    npgsqlOptions => npgsqlOptions
                        .MigrationsHistoryTable(HistoryRepository.DefaultTableName))
                .UseSnakeCaseNamingConvention()
                .AddInterceptors(sp.GetRequiredService<InsertOutboxMessagesInterceptor>()));

        services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<SupportDbContext>());

        services.AddScoped<ITicketsRepository, TicketsRepository>();

        services.AddScoped<ISupportAgentRepository, SupportAgentRepository>();

        services.AddScoped<ISupportAuditRepository, SupportAuditRepository>();

        services.AddScoped<IRefundRequestRepository, RefundRequestRepository>();

        services.AddScoped<IOrderSnapshotRepository, OrderSnapshotRepository>();

        // One implementation, called by every command handler that changes a ticket. Registered
        // here rather than discovered, because "the audit entry commits with the change" is a
        // property of there being exactly one of these.
        services.AddScoped<ISupportAuditWriter, SupportAuditWriter>();

        services.AddScoped<ISupportContext, SupportContext>();

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

using FoodDeliveryService.Common.Application.Authorization;
using FoodDeliveryService.Common.Application.EventBus;
using FoodDeliveryService.Common.Application.Messaging;
using FoodDeliveryService.Common.Infrastructure.Outbox;
using FoodDeliveryService.Common.Presentation.Endpoints;
using FoodDeliveryService.Modules.Payments.Application.Abstractions.Authentication;
using FoodDeliveryService.Modules.Payments.Application.Abstractions.Data;
using FoodDeliveryService.Modules.Payments.Domain.PaymentMethods;
using FoodDeliveryService.Modules.Payments.Infrastructure.Authentication;
using FoodDeliveryService.Modules.Payments.Infrastructure.Authorization;
using FoodDeliveryService.Modules.Payments.Infrastructure.Database;
using FoodDeliveryService.Modules.Payments.Infrastructure.Inbox;
using FoodDeliveryService.Modules.Payments.Infrastructure.Outbox;
using FoodDeliveryService.Modules.Payments.Infrastructure.PaymentMethods;
using FoodDeliveryService.Modules.Payments.Infrastructure.Stripe;
using FoodDeliveryService.Modules.Users.IntegrationEvents;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace FoodDeliveryService.Modules.Payments.Infrastructure;

public static class PaymentsModule
{
    public static IServiceCollection AddPaymentsModule(
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
            // Every registered user gets a Stripe customer, so the card form has something to attach
            // to when they first reach it (§6.2). The remaining subscriptions arrive one per
            // milestone, each one line here: OrderPlaced (§8), OrderAccepted/Rejected/Cancelled (§9)
            // and RefundApproved (§10). Every one of them must also be drawn in the README's C3
            // event topology in the same change — IntegrationEventTopologyTests diffs the two.
            registration.AddConsumer<IntegrationEventConsumer<UserRegisteredIntegrationEvent>>()
                .Endpoint(c => c.InstanceId = instanceId);

            // The explicit request client is not optional even for a service that consumes little:
            // without it MassTransit's implicit IRequestClient<T> resolution silently fails to route
            // the request and every permission lookup times out, which surfaces as a blanket 403
            // rather than as an error.
            registration.AddRequestClient<GetUserPermissionsRequest>();
        };
    }

    private static void AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<PaymentsDbContext>((sp, options) =>
            options
                .UseNpgsql(
                    configuration.GetConnectionString("Database"),
                    npgsqlOptions => npgsqlOptions
                        .MigrationsHistoryTable(HistoryRepository.DefaultTableName))
                .UseSnakeCaseNamingConvention()
                .AddInterceptors(sp.GetRequiredService<InsertOutboxMessagesInterceptor>()));

        services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<PaymentsDbContext>());

        services.AddScoped<ICustomerPaymentProfileRepository, CustomerPaymentProfileRepository>();

        // The remaining repositories land here alongside their aggregates, one AddScoped each:
        // IPaymentRepository (§8), IStripeEventLogRepository (§7) and IRefundRepository (§10).

        services.AddScoped<IPaymentsContext, PaymentsContext>();

        services.AddScoped<IPermissionService, PermissionService>();

        services.AddStripe(configuration);

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

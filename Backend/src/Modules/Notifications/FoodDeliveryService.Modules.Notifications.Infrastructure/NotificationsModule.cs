using FoodDeliveryService.Common.Application.Authorization;
using FoodDeliveryService.Common.Application.EventBus;
using FoodDeliveryService.Common.Application.Messaging;
using FoodDeliveryService.Common.Infrastructure.Outbox;
using FoodDeliveryService.Common.Presentation.Endpoints;
using FoodDeliveryService.Modules.Notifications.Application.Abstractions.Authentication;
using FoodDeliveryService.Modules.Notifications.Application.Abstractions.Data;
using FoodDeliveryService.Modules.Notifications.Application.Abstractions.Email;
using FoodDeliveryService.Modules.Notifications.Application.Abstractions.Notifications;
using FoodDeliveryService.Modules.Notifications.Domain.Notifications;
using FoodDeliveryService.Modules.Notifications.Infrastructure.Authentication;
using FoodDeliveryService.Modules.Notifications.Infrastructure.Database;
using FoodDeliveryService.Modules.Notifications.Infrastructure.Email;
using FoodDeliveryService.Modules.Notifications.Infrastructure.Inbox;
using FoodDeliveryService.Modules.Notifications.Infrastructure.Notifications;
using FoodDeliveryService.Modules.Notifications.Infrastructure.Outbox;
using FoodDeliveryService.Modules.Notifications.Infrastructure.RecipientUsers;
using FoodDeliveryService.Modules.Notifications.Domain.RecipientUsers;
using FoodDeliveryService.Modules.Users.IntegrationEvents;
using MassTransit;
using MassTransit.Configuration;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace FoodDeliveryService.Modules.Notifications.Infrastructure;

public static class NotificationsModule
{
    public static IServiceCollection AddNotificationsModule(
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
            registration.AddConsumer<IntegrationEventConsumer<UserInvitedIntegrationEvent>>()
                .Endpoint(c => c.InstanceId = instanceId);
        };
    }

    private static void AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<NotificationsDbContext>((sp, options) =>
            options
                .UseNpgsql(
                    configuration.GetConnectionString("Database"),
                    npgsqlOptions => npgsqlOptions
                        .MigrationsHistoryTable(HistoryRepository.DefaultTableName))
                .UseSnakeCaseNamingConvention()
                .AddInterceptors(sp.GetRequiredService<InsertOutboxMessagesInterceptor>()));

        services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<NotificationsDbContext>());

        services.AddScoped<INotificationsRepository, NotificationsRepository>();

        services.AddScoped<IRecipientUserRepository, RecipientUsersRepository>();

        services.AddScoped<INotificationContext, NotificationsContext>();

        // Email sender (dev: logs the message). Instrumented via its own ActivitySource; registered
        // singleton as it holds no per-request state. InvitationEmail options keep the activation-link
        // base URL; Email options select the provider (Log now, SMTP/SendGrid later).
        services.Configure<InvitationEmailOptions>(configuration.GetSection(InvitationEmailOptions.SectionName));

        services.Configure<EmailOptions>(configuration.GetSection(EmailOptions.SectionName));

        services.AddSingleton<IEmailService, EmailService>();

        // Notification send pipeline: the template renderer plus every delivery channel. Only the Email
        // channel is registered now; Phase 2 adds SignalR/push channels here and the send handler picks
        // them up through IEnumerable<INotificationChannel>.
        services.AddSingleton<INotificationTemplateRenderer, NotificationTemplateRenderer>();

        services.AddScoped<INotificationChannel, EmailNotificationChannel>();

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

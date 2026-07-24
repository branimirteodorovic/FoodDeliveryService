using FoodDeliveryService.Common.Application.Authorization;
using FoodDeliveryService.Common.Application.EventBus;
using FoodDeliveryService.Common.Presentation.Endpoints;
using FoodDeliveryService.Modules.RealTime.Application.Abstractions.Data;
using FoodDeliveryService.Modules.RealTime.Application.RealTime;
using FoodDeliveryService.Modules.RealTime.Infrastructure.Authorization;
using FoodDeliveryService.Modules.RealTime.Infrastructure.Consumers;
using FoodDeliveryService.Modules.RealTime.Infrastructure.Database;
using FoodDeliveryService.Modules.RealTime.Infrastructure.Database.RestaurantManagers;
using FoodDeliveryService.Modules.RealTime.Infrastructure.Inbox;
using FoodDeliveryService.Modules.RealTime.Infrastructure.RealTime;
using FoodDeliveryService.Modules.Restaurants.IntegrationEvents;
using FoodDeliveryService.Modules.Users.IntegrationEvents;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace FoodDeliveryService.Modules.RealTime.Infrastructure;

/// <summary>
/// Registration surface for the Real-Time module. Unlike the other modules this one owns no
/// aggregates and no outbox — it maps the tracking hub and wires direct bus consumers that fan out
/// to SignalR groups (Milestone B: Orders lifecycle → customer timeline; Milestone C: Delivery
/// lifecycle → driver tracking). From Milestone D it owns its first (and only) database — a minimal
/// RestaurantManager replica, consumed durably via the inbox, unlike every other consumer here.
/// </summary>
public static class RealTimeModule
{
    public static IServiceCollection AddRealTimeModule(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddIntegrationEventHandlers();

        services.AddInfrastructure(configuration);

        services.AddEndpoints(Presentation.AssemblyReference.Assembly);

        return services;
    }

#pragma warning disable IDE0060 // Remove unused parameter — redisConnectionString is fixed by AddInfrastructure's moduleConfigureConsumers contract (other modules use it for saga Redis repositories); this service has none.
    public static void ConfigureConsumers(IRegistrationConfigurator registrationConfigurator, string instanceId, string redisConnectionString)
#pragma warning restore IDE0060 // Remove unused parameter
    {
        // The permission request client CustomClaimsTransformation uses to resolve the connecting
        // user from the Users service on the authenticated handshake. Without an explicit
        // registration MassTransit cannot route the request and every handshake times out.
        registrationConfigurator.AddRequestClient<GetUserPermissionsRequest>();

        // Direct consumers for the Orders lifecycle — each on its OWN queue (the instanceId suffix),
        // so this service gets a full fan-out copy of every event rather than competing with other
        // subscribers. These broadcast immediately and never touch the inbox (see OrderStatusConsumer).
        registrationConfigurator.AddConsumer<OrderPlacedConsumer>()
            .Endpoint(c => c.InstanceId = instanceId);
        registrationConfigurator.AddConsumer<OrderAcceptedConsumer>()
            .Endpoint(c => c.InstanceId = instanceId);
        registrationConfigurator.AddConsumer<OrderRejectedConsumer>()
            .Endpoint(c => c.InstanceId = instanceId);
        registrationConfigurator.AddConsumer<OrderReadyForPickupConsumer>()
            .Endpoint(c => c.InstanceId = instanceId);
        registrationConfigurator.AddConsumer<OrderCancelledConsumer>()
            .Endpoint(c => c.InstanceId = instanceId);

        // Milestone C: the driver-binding consumers for Delivery's own lifecycle events. Same
        // direct-consumer, own-queue pattern as the Orders consumers above.
        registrationConfigurator.AddConsumer<DriverAssignedConsumer>()
            .Endpoint(c => c.InstanceId = instanceId);
        registrationConfigurator.AddConsumer<OrderPickedUpConsumer>()
            .Endpoint(c => c.InstanceId = instanceId);
        registrationConfigurator.AddConsumer<OrderDeliveredConsumer>()
            .Endpoint(c => c.InstanceId = instanceId);

        // Milestone D: unlike every consumer above, the RestaurantManager replica must survive a
        // cold start reliably, so these two go through the durable inbox instead of broadcasting
        // directly — a deliberate, localized exception to this module's "all direct" rule (see the
        // plan's §5.1 justification).
        registrationConfigurator.AddConsumer<IntegrationEventConsumer<RestaurantRegisteredIntegrationEvent>>()
            .Endpoint(c => c.InstanceId = instanceId);
        registrationConfigurator.AddConsumer<IntegrationEventConsumer<RestaurantAddressUpdatedIntegrationEvent>>()
            .Endpoint(c => c.InstanceId = instanceId);
    }

    private static void AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        // Resolves the connecting principal's module-side id + permissions over the bus (see
        // PermissionService). Required because CustomClaimsTransformation runs on every
        // authenticated request, the hub negotiate included.
        services.AddScoped<IPermissionService, PermissionService>();

        // Fan-out over the SignalR hub + the ephemeral order→customer routing map in Redis. Both are
        // stateless singletons: the notifier wraps IHubContext, the map wraps ICacheService.
        services.AddSingleton<IRealTimeNotifier, RealTimeNotifier>();
        services.AddSingleton<IOrderRoutingMap, OrderRoutingMap>();

        // Milestone C: the ephemeral driver→customer binding, and the hosted subscriber that reads
        // Delivery's Redis pub/sub location stream and forwards positions through it.
        services.AddSingleton<IDriverBindingStore, DriverBindingStore>();
        services.AddHostedService<DriverLocationSubscriber>();

        // Milestone D: the service's first database — the RestaurantManager replica, kept current
        // from Restaurants' events via the inbox above. No InsertOutboxMessagesInterceptor: this
        // service raises no domain events and publishes no integration events of its own.
        services.AddDbContext<RealTimeDbContext>((_, options) =>
            options
                .UseNpgsql(
                    configuration.GetConnectionString("Database"),
                    npgsqlOptions => npgsqlOptions
                        .MigrationsHistoryTable(HistoryRepository.DefaultTableName))
                .UseSnakeCaseNamingConvention());

        services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<RealTimeDbContext>());

        services.AddScoped<IRestaurantManagerStore, RestaurantManagerStore>();

        services.Configure<InboxOptions>(configuration.GetSection("MessageProcessor:Inbox"));

        services.ConfigureOptions<ConfigureProcessInboxJob>();
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

using FoodDeliveryService.Common.Application.Authorization;
using FoodDeliveryService.Common.Presentation.Endpoints;
using FoodDeliveryService.Modules.RealTime.Application.RealTime;
using FoodDeliveryService.Modules.RealTime.Infrastructure.Authorization;
using FoodDeliveryService.Modules.RealTime.Infrastructure.Consumers;
using FoodDeliveryService.Modules.RealTime.Infrastructure.RealTime;
using FoodDeliveryService.Modules.Users.IntegrationEvents;
using MassTransit;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace FoodDeliveryService.Modules.RealTime.Infrastructure;

/// <summary>
/// Registration surface for the Real-Time module. Unlike the other modules this one owns no
/// database, no aggregates and no outbox/inbox — it maps the tracking hub and wires direct bus
/// consumers that fan out to SignalR groups (Milestone B: the Orders lifecycle → customer timeline).
/// The fan-out goes through <see cref="IRealTimeNotifier"/> (implemented over the SignalR hub) and
/// every transition warms the ephemeral <see cref="IOrderRoutingMap"/> in Redis.
/// </summary>
public static class RealTimeModule
{
#pragma warning disable IDE0060 // Remove unused parameter — kept for parity with every other Add{Module} and for Milestone D, which introduces the RestaurantManager replica DbContext bound from configuration.
    public static IServiceCollection AddRealTimeModule(
        this IServiceCollection services,
        IConfiguration configuration)
#pragma warning restore IDE0060 // Remove unused parameter
    {
        services.AddInfrastructure();

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
    }

    private static void AddInfrastructure(this IServiceCollection services)
    {
        // Resolves the connecting principal's module-side id + permissions over the bus (see
        // PermissionService). Required because CustomClaimsTransformation runs on every
        // authenticated request, the hub negotiate included.
        services.AddScoped<IPermissionService, PermissionService>();

        // Fan-out over the SignalR hub + the ephemeral order→customer routing map in Redis. Both are
        // stateless singletons: the notifier wraps IHubContext, the map wraps ICacheService.
        services.AddSingleton<IRealTimeNotifier, RealTimeNotifier>();
        services.AddSingleton<IOrderRoutingMap, OrderRoutingMap>();
    }
}

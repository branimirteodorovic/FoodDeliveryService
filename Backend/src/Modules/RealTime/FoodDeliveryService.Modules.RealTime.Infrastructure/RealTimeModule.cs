using FoodDeliveryService.Common.Application.Authorization;
using FoodDeliveryService.Common.Presentation.Endpoints;
using FoodDeliveryService.Modules.RealTime.Infrastructure.Authorization;
using FoodDeliveryService.Modules.Users.IntegrationEvents;
using MassTransit;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace FoodDeliveryService.Modules.RealTime.Infrastructure;

/// <summary>
/// Registration surface for the Real-Time module. Unlike the other modules this one owns no
/// database, no aggregates and no outbox/inbox — it maps the tracking hub and (from Milestone B)
/// wires direct bus consumers that fan out to SignalR groups. All it needs at Milestone A is the
/// hub endpoint plus the permission RPC that CustomClaimsTransformation uses to resolve a
/// connecting user's module-side id.
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

#pragma warning disable IDE0060 // Remove unused parameter — signature is fixed by AddInfrastructure's moduleConfigureConsumers contract; instanceId/redisConnectionString are used once consumers land in Milestone B.
    public static void ConfigureConsumers(IRegistrationConfigurator registrationConfigurator, string instanceId, string redisConnectionString)
#pragma warning restore IDE0060 // Remove unused parameter
    {
        // No event consumers yet — Milestone B adds the Orders lifecycle consumers that broadcast
        // status frames. The one thing the hub needs today is the permission request client that
        // CustomClaimsTransformation uses to resolve the connecting user from the Users service.
        // Without an explicit request-client registration, MassTransit cannot route the request and
        // every handshake times out.
        registrationConfigurator.AddRequestClient<GetUserPermissionsRequest>();
    }

    private static void AddInfrastructure(this IServiceCollection services)
    {
        // Resolves the connecting principal's module-side id + permissions over the bus (see
        // PermissionService). Required because CustomClaimsTransformation runs on every
        // authenticated request, the hub negotiate included.
        services.AddScoped<IPermissionService, PermissionService>();
    }
}

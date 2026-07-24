using FoodDeliveryService.Common.Application.EventBus;
using FoodDeliveryService.Modules.RealTime.Application.RealTime;
using FoodDeliveryService.Modules.Restaurants.IntegrationEvents;

namespace FoodDeliveryService.Modules.RealTime.Presentation.Managers;

/// <summary>
/// Maintains the local RestaurantManager replica (dispatched by ProcessInboxJob, idempotent via the
/// inbox — Milestone D's deliberate exception to this module's "all direct consumers" rule, since
/// this mapping must survive a cold start reliably).
/// </summary>
internal sealed class RestaurantRegisteredIntegrationEventHandler(IRestaurantManagerStore store)
    : IntegrationEventHandler<RestaurantRegisteredIntegrationEvent>
{
    public override Task Handle(
        RestaurantRegisteredIntegrationEvent integrationEvent,
        CancellationToken cancellationToken = default) =>
        store.UpsertAsync(
            integrationEvent.ManagerUserId,
            integrationEvent.RestaurantId,
            integrationEvent.Name,
            cancellationToken);
}

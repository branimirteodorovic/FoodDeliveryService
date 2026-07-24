using FoodDeliveryService.Common.Application.EventBus;
using FoodDeliveryService.Modules.RealTime.Application.RealTime;
using FoodDeliveryService.Modules.Restaurants.IntegrationEvents;

namespace FoodDeliveryService.Modules.RealTime.Presentation.Managers;

/// <summary>
/// Keeps the RestaurantManager replica's name in sync. Resolved by RestaurantId (this event carries
/// no ManagerUserId) — a no-op if the restaurant hasn't been registered here yet.
/// </summary>
internal sealed class RestaurantAddressUpdatedIntegrationEventHandler(IRestaurantManagerStore store)
    : IntegrationEventHandler<RestaurantAddressUpdatedIntegrationEvent>
{
    public override Task Handle(
        RestaurantAddressUpdatedIntegrationEvent integrationEvent,
        CancellationToken cancellationToken = default) =>
        store.UpdateRestaurantNameAsync(
            integrationEvent.RestaurantId,
            integrationEvent.Name,
            cancellationToken);
}

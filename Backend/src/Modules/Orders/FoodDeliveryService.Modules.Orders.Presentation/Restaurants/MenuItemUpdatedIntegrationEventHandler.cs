using FoodDeliveryService.Common.Application.EventBus;
using FoodDeliveryService.Common.Domain;
using FoodDeliveryService.Modules.Orders.Application.Restaurants.UpsertMenuItem;
using FoodDeliveryService.Modules.Restaurants.IntegrationEvents;
using MediatR;

namespace FoodDeliveryService.Modules.Orders.Presentation.Restaurants;

/// <summary>
/// Keeps the MenuItem replica's name/price current (dispatched by ProcessInboxJob, idempotent via
/// the inbox). The event carries a full snapshot, so this is the same upsert as MenuItemAdded —
/// which also makes it safe when the events race across their separate queues.
/// </summary>
internal sealed class MenuItemUpdatedIntegrationEventHandler(ISender sender)
    : IntegrationEventHandler<MenuItemUpdatedIntegrationEvent>
{
    public override async Task Handle(
        MenuItemUpdatedIntegrationEvent integrationEvent,
        CancellationToken cancellationToken = default)
    {
        Result result = await sender.Send(
            new UpsertMenuItemCommand(
                integrationEvent.MenuItemId,
                integrationEvent.RestaurantId,
                integrationEvent.Name,
                integrationEvent.Price,
                integrationEvent.IsAvailable),
            cancellationToken);

        if (result.IsFailure)
        {
            throw new Common.Application.Exceptions.ApplicationException(
                nameof(UpsertMenuItemCommand),
                result.Error);
        }
    }
}

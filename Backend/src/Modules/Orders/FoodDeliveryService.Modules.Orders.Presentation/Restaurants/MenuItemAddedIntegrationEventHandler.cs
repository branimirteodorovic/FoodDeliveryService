using FoodDeliveryService.Common.Application.EventBus;
using FoodDeliveryService.Common.Domain;
using FoodDeliveryService.Modules.Orders.Application.Restaurants.UpsertMenuItem;
using FoodDeliveryService.Modules.Restaurants.IntegrationEvents;
using MediatR;

namespace FoodDeliveryService.Modules.Orders.Presentation.Restaurants;

/// <summary>
/// Seeds the local MenuItem replica (dispatched by ProcessInboxJob, idempotent via the inbox) —
/// the source of truth for server-side pricing and availability checks at placement.
/// </summary>
internal sealed class MenuItemAddedIntegrationEventHandler(ISender sender)
    : IntegrationEventHandler<MenuItemAddedIntegrationEvent>
{
    public override async Task Handle(
        MenuItemAddedIntegrationEvent integrationEvent,
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

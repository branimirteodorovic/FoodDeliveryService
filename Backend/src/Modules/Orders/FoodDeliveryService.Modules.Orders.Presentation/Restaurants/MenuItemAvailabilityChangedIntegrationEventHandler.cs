using FoodDeliveryService.Common.Application.EventBus;
using FoodDeliveryService.Common.Domain;
using FoodDeliveryService.Modules.Orders.Application.Restaurants.UpdateMenuItemAvailability;
using FoodDeliveryService.Modules.Restaurants.IntegrationEvents;
using MediatR;

namespace FoodDeliveryService.Modules.Orders.Presentation.Restaurants;

/// <summary>
/// Syncs the MenuItem replica's availability flag (dispatched by ProcessInboxJob, idempotent via
/// the inbox). An unknown item fails the command — and thus throws — so the inbox retries until
/// the item's Added event has been consumed.
/// </summary>
internal sealed class MenuItemAvailabilityChangedIntegrationEventHandler(ISender sender)
    : IntegrationEventHandler<MenuItemAvailabilityChangedIntegrationEvent>
{
    public override async Task Handle(
        MenuItemAvailabilityChangedIntegrationEvent integrationEvent,
        CancellationToken cancellationToken = default)
    {
        Result result = await sender.Send(
            new UpdateMenuItemAvailabilityCommand(
                integrationEvent.MenuItemId,
                integrationEvent.IsAvailable),
            cancellationToken);

        if (result.IsFailure)
        {
            throw new Common.Application.Exceptions.ApplicationException(
                nameof(UpdateMenuItemAvailabilityCommand),
                result.Error);
        }
    }
}

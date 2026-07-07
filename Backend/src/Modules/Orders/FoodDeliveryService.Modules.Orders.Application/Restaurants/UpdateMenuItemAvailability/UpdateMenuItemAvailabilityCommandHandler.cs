using FoodDeliveryService.Common.Application.Messaging;
using FoodDeliveryService.Common.Domain;
using FoodDeliveryService.Modules.Orders.Application.Abstractions.Data;
using FoodDeliveryService.Modules.Orders.Domain.Restaurants;

namespace FoodDeliveryService.Modules.Orders.Application.Restaurants.UpdateMenuItemAvailability;

internal sealed class UpdateMenuItemAvailabilityCommandHandler(
    IMenuItemReplicaRepository menuItemRepository,
    IUnitOfWork unitOfWork)
    : ICommandHandler<UpdateMenuItemAvailabilityCommand>
{
    public async Task<Result> Handle(UpdateMenuItemAvailabilityCommand request, CancellationToken cancellationToken)
    {
        MenuItem? menuItem = await menuItemRepository.GetAsync(request.MenuItemId, cancellationToken);

        // Unlike profile updates (where unknown users are expected), an availability change for an
        // unknown item means its Added event has not landed yet — fail so the inbox retries later.
        if (menuItem is null)
        {
            return Result.Failure(MenuItemErrors.NotFound(request.MenuItemId));
        }

        menuItem.SetAvailability(request.IsAvailable);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}

using FoodDeliveryService.Common.Application.Messaging;
using FoodDeliveryService.Common.Domain;
using FoodDeliveryService.Modules.Orders.Application.Abstractions.Data;
using FoodDeliveryService.Modules.Orders.Domain.Restaurants;

namespace FoodDeliveryService.Modules.Orders.Application.Restaurants.UpsertMenuItem;

internal sealed class UpsertMenuItemCommandHandler(
    IMenuItemReplicaRepository menuItemRepository,
    IUnitOfWork unitOfWork)
    : ICommandHandler<UpsertMenuItemCommand>
{
    public async Task<Result> Handle(UpsertMenuItemCommand request, CancellationToken cancellationToken)
    {
        MenuItem? menuItem = await menuItemRepository.GetAsync(request.MenuItemId, cancellationToken);

        if (menuItem is null)
        {
            menuItemRepository.Insert(
                MenuItem.Create(request.MenuItemId, request.RestaurantId, request.Name, request.Price, request.IsAvailable));
        }
        else
        {
            menuItem.Update(request.Name, request.Price, request.IsAvailable);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}

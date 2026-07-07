using FoodDeliveryService.Common.Application.Messaging;
using FoodDeliveryService.Common.Domain;
using FoodDeliveryService.Modules.Restaurants.Application.Abstractions.Data;
using FoodDeliveryService.Modules.Restaurants.Domain.Managers;

namespace FoodDeliveryService.Modules.Restaurants.Application.Managers.UpsertManager;

internal sealed class UpsertRestaurantManagerCommandHandler(
    IRestaurantManagersRepository restaurantManagersRepository,
    IUnitOfWork unitOfWork)
    : ICommandHandler<UpsertRestaurantManagerCommand>
{
    public async Task<Result> Handle(UpsertRestaurantManagerCommand request, CancellationToken cancellationToken)
    {
        RestaurantManager? manager = await restaurantManagersRepository.GetAsync(request.UserId, cancellationToken);

        if (manager is null)
        {
            restaurantManagersRepository.Insert(
                RestaurantManager.Create(request.UserId, request.Email, request.FirstName, request.LastName));
        }
        else
        {
            manager.Update(request.FirstName, request.LastName);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}

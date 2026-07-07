using FoodDeliveryService.Common.Application.Messaging;
using FoodDeliveryService.Common.Domain;
using FoodDeliveryService.Modules.Restaurants.Application.Abstractions.Data;
using FoodDeliveryService.Modules.Restaurants.Domain.Managers;

namespace FoodDeliveryService.Modules.Restaurants.Application.Managers.UpdateManager;

internal sealed class UpdateRestaurantManagerCommandHandler(
    IRestaurantManagersRepository restaurantManagersRepository,
    IUnitOfWork unitOfWork)
    : ICommandHandler<UpdateRestaurantManagerCommand>
{
    public async Task<Result> Handle(UpdateRestaurantManagerCommand request, CancellationToken cancellationToken)
    {
        RestaurantManager? manager = await restaurantManagersRepository.GetAsync(request.UserId, cancellationToken);

        // UserProfileUpdated fires for every actor (customers included); only managers are
        // replicated here, so an unknown user is a deliberate no-op, not an error.
        if (manager is null)
        {
            return Result.Success();
        }

        manager.Update(request.FirstName, request.LastName);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}

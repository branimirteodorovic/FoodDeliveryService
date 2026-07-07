using FoodDeliveryService.Common.Application.Messaging;
using FoodDeliveryService.Common.Domain;
using FoodDeliveryService.Modules.Restaurants.Application.Abstractions.Authentication;
using FoodDeliveryService.Modules.Restaurants.Application.Abstractions.Data;
using FoodDeliveryService.Modules.Restaurants.Domain.Restaurants;

namespace FoodDeliveryService.Modules.Restaurants.Application.Restaurants.CreateMenuItem;

internal sealed class CreateMenuItemCommandHandler(
    IRestaurantsRepository restaurantsRepository,
    IRestaurantsContext restaurantsContext,
    IUnitOfWork unitOfWork)
    : ICommandHandler<CreateMenuItemCommand, Guid>
{
    public async Task<Result<Guid>> Handle(CreateMenuItemCommand request, CancellationToken cancellationToken)
    {
        Restaurant? restaurant = await restaurantsRepository.GetAsync(request.RestaurantId, cancellationToken);

        if (restaurant is null)
        {
            return Result.Failure<Guid>(RestaurantErrors.NotFound(request.RestaurantId));
        }

        Result ownershipResult = RestaurantOwnership.EnsureCanModify(restaurant, restaurantsContext);

        if (ownershipResult.IsFailure)
        {
            return Result.Failure<Guid>(ownershipResult.Error);
        }

        Result<MenuItem> itemResult = restaurant.AddMenuItem(
            request.CategoryId,
            request.Name,
            request.Description,
            request.Price,
            request.PhotoUrl,
            request.IsAvailable);

        if (itemResult.IsFailure)
        {
            return Result.Failure<Guid>(itemResult.Error);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return itemResult.Value.Id;
    }
}

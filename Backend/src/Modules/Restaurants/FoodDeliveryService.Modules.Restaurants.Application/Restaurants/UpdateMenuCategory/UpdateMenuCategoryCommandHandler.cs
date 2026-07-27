using FoodDeliveryService.Common.Application.Caching;
using FoodDeliveryService.Common.Application.Messaging;
using FoodDeliveryService.Common.Domain;
using FoodDeliveryService.Modules.Restaurants.Application.Abstractions.Authentication;
using FoodDeliveryService.Modules.Restaurants.Application.Abstractions.Data;
using FoodDeliveryService.Modules.Restaurants.Application.Caching;
using FoodDeliveryService.Modules.Restaurants.Domain.Restaurants;

namespace FoodDeliveryService.Modules.Restaurants.Application.Restaurants.UpdateMenuCategory;

internal sealed class UpdateMenuCategoryCommandHandler(
    IRestaurantsRepository restaurantsRepository,
    IRestaurantsContext restaurantsContext,
    IUnitOfWork unitOfWork,
    ICacheService cacheService)
    : ICommandHandler<UpdateMenuCategoryCommand>
{
    public async Task<Result> Handle(UpdateMenuCategoryCommand request, CancellationToken cancellationToken)
    {
        Restaurant? restaurant = await restaurantsRepository.GetAsync(request.RestaurantId, cancellationToken);

        if (restaurant is null)
        {
            return Result.Failure(RestaurantErrors.NotFound(request.RestaurantId));
        }

        Result ownershipResult = RestaurantOwnership.EnsureCanModify(restaurant, restaurantsContext);

        if (ownershipResult.IsFailure)
        {
            return ownershipResult;
        }

        Result updateResult = restaurant.UpdateMenuCategory(request.CategoryId, request.Name, request.DisplayOrder);

        if (updateResult.IsFailure)
        {
            return updateResult;
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        // A rename or reorder changes both the category's label and the order categories come back
        // in, so the whole composed menu has to be rebuilt on the next read.
        await cacheService.RemoveAsync(RestaurantCacheKeys.Menu(request.RestaurantId), cancellationToken);

        return Result.Success();
    }
}

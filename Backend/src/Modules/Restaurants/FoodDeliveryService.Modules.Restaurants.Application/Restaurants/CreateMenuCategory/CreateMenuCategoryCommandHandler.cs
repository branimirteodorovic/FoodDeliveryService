using FoodDeliveryService.Common.Application.Caching;
using FoodDeliveryService.Common.Application.Messaging;
using FoodDeliveryService.Common.Domain;
using FoodDeliveryService.Modules.Restaurants.Application.Abstractions.Authentication;
using FoodDeliveryService.Modules.Restaurants.Application.Abstractions.Data;
using FoodDeliveryService.Modules.Restaurants.Application.Caching;
using FoodDeliveryService.Modules.Restaurants.Domain.Restaurants;

namespace FoodDeliveryService.Modules.Restaurants.Application.Restaurants.CreateMenuCategory;

internal sealed class CreateMenuCategoryCommandHandler(
    IRestaurantsRepository restaurantsRepository,
    IRestaurantsContext restaurantsContext,
    IUnitOfWork unitOfWork,
    ICacheService cacheService)
    : ICommandHandler<CreateMenuCategoryCommand, Guid>
{
    public async Task<Result<Guid>> Handle(CreateMenuCategoryCommand request, CancellationToken cancellationToken)
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

        Result<MenuCategory> categoryResult = restaurant.AddMenuCategory(request.Name, request.DisplayOrder);

        if (categoryResult.IsFailure)
        {
            return Result.Failure<Guid>(categoryResult.Error);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        // The composed menu is grouped by category, so a new (empty) category changes its shape.
        await cacheService.RemoveAsync(RestaurantCacheKeys.Menu(request.RestaurantId), cancellationToken);

        return categoryResult.Value.Id;
    }
}

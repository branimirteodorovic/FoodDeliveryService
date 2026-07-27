using FoodDeliveryService.Common.Application.Caching;
using FoodDeliveryService.Common.Application.Messaging;
using FoodDeliveryService.Common.Domain;
using FoodDeliveryService.Modules.Restaurants.Application.Abstractions.Authentication;
using FoodDeliveryService.Modules.Restaurants.Application.Abstractions.Data;
using FoodDeliveryService.Modules.Restaurants.Application.Caching;
using FoodDeliveryService.Modules.Restaurants.Domain.Restaurants;

namespace FoodDeliveryService.Modules.Restaurants.Application.Restaurants.CreateMenuItem;

internal sealed class CreateMenuItemCommandHandler(
    IRestaurantsRepository restaurantsRepository,
    IRestaurantsContext restaurantsContext,
    IUnitOfWork unitOfWork,
    ICacheService cacheService)
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

        // The new item belongs to this restaurant's composed menu, so the cached menu is now
        // missing a row — evicted inline, matching how UpdateRestaurant/UpdateMenuItem invalidate
        // their own keys. No `restaurants:item:{id}` evict is needed: the id is brand new.
        await cacheService.RemoveAsync(RestaurantCacheKeys.Menu(request.RestaurantId), cancellationToken);

        return itemResult.Value.Id;
    }
}

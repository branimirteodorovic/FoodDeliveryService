using FoodDeliveryService.Common.Application.Caching;
using FoodDeliveryService.Common.Application.Messaging;
using FoodDeliveryService.Common.Domain;
using FoodDeliveryService.Modules.Restaurants.Application.Abstractions.Authentication;
using FoodDeliveryService.Modules.Restaurants.Application.Abstractions.Data;
using FoodDeliveryService.Modules.Restaurants.Application.Caching;
using FoodDeliveryService.Modules.Restaurants.Domain.Restaurants;

namespace FoodDeliveryService.Modules.Restaurants.Application.Restaurants.SetMenuItemAvailability;

internal sealed class SetMenuItemAvailabilityCommandHandler(
    IRestaurantsRepository restaurantsRepository,
    IRestaurantsContext restaurantsContext,
    IUnitOfWork unitOfWork,
    ICacheService cacheService)
    : ICommandHandler<SetMenuItemAvailabilityCommand>
{
    public async Task<Result> Handle(SetMenuItemAvailabilityCommand request, CancellationToken cancellationToken)
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

        Result availabilityResult = restaurant.SetMenuItemAvailability(request.MenuItemId, request.IsAvailable);

        if (availabilityResult.IsFailure)
        {
            return availabilityResult;
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        // Evicted inline so a later UpdateMenuItem on the same item can't have its
        // MenuItemUpdated/MenuItemPriceChangedDomainEventHandler snapshot read pick up this
        // availability change's now-stale cache entry (see UpdateMenuItemCommandHandler).
        await cacheService.RemoveAsync(RestaurantCacheKeys.Item(request.MenuItemId), cancellationToken);

        // The composed menu carries each item's availability flag, so selling an item out has to
        // invalidate it too — otherwise browsers keep seeing the item as orderable (Milestone C).
        await cacheService.RemoveAsync(RestaurantCacheKeys.Menu(request.RestaurantId), cancellationToken);

        return Result.Success();
    }
}

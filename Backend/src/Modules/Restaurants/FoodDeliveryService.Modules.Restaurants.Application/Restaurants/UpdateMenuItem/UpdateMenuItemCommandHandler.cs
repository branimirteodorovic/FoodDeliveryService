using FoodDeliveryService.Common.Application.Caching;
using FoodDeliveryService.Common.Application.Messaging;
using FoodDeliveryService.Common.Domain;
using FoodDeliveryService.Modules.Restaurants.Application.Abstractions.Authentication;
using FoodDeliveryService.Modules.Restaurants.Application.Abstractions.Data;
using FoodDeliveryService.Modules.Restaurants.Application.Caching;
using FoodDeliveryService.Modules.Restaurants.Domain.Restaurants;

namespace FoodDeliveryService.Modules.Restaurants.Application.Restaurants.UpdateMenuItem;

internal sealed class UpdateMenuItemCommandHandler(
    IRestaurantsRepository restaurantsRepository,
    IRestaurantsContext restaurantsContext,
    IUnitOfWork unitOfWork,
    ICacheService cacheService)
    : ICommandHandler<UpdateMenuItemCommand>
{
    public async Task<Result> Handle(UpdateMenuItemCommand request, CancellationToken cancellationToken)
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

        Result updateResult = restaurant.UpdateMenuItem(
            request.MenuItemId,
            request.Name,
            request.Description,
            request.Price,
            request.PhotoUrl);

        if (updateResult.IsFailure)
        {
            return updateResult;
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        // Evicted inline for the same reason as UpdateRestaurantCommandHandler: MenuItemUpdated/
        // MenuItemPriceChangedDomainEventHandler read this cached query via ISender to build their
        // integration-event snapshot, so a stale cache entry here would leak into that snapshot.
        await cacheService.RemoveAsync(RestaurantCacheKeys.Item(request.MenuItemId), cancellationToken);

        return Result.Success();
    }
}

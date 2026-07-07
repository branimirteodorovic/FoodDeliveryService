using FoodDeliveryService.Common.Application.Messaging;
using FoodDeliveryService.Common.Domain;
using FoodDeliveryService.Modules.Restaurants.Application.Abstractions.Authentication;
using FoodDeliveryService.Modules.Restaurants.Application.Abstractions.Data;
using FoodDeliveryService.Modules.Restaurants.Domain.Restaurants;

namespace FoodDeliveryService.Modules.Restaurants.Application.Restaurants.CreateMenuCategory;

internal sealed class CreateMenuCategoryCommandHandler(
    IRestaurantsRepository restaurantsRepository,
    IRestaurantsContext restaurantsContext,
    IUnitOfWork unitOfWork)
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

        return categoryResult.Value.Id;
    }
}

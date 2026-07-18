using FoodDeliveryService.Common.Application.Messaging;
using FoodDeliveryService.Common.Domain;
using FoodDeliveryService.Modules.Restaurants.Application.Abstractions.Authentication;
using FoodDeliveryService.Modules.Restaurants.Application.Abstractions.Data;
using FoodDeliveryService.Modules.Restaurants.Domain.Restaurants;

namespace FoodDeliveryService.Modules.Restaurants.Application.Restaurants.UpdateRestaurant;

internal sealed class UpdateRestaurantCommandHandler(
    IRestaurantsRepository restaurantsRepository,
    IRestaurantsContext restaurantsContext,
    IUnitOfWork unitOfWork)
    : ICommandHandler<UpdateRestaurantCommand>
{
    public async Task<Result> Handle(UpdateRestaurantCommand request, CancellationToken cancellationToken)
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

        Result detailsResult = restaurant.UpdateDetails(
            request.Name,
            request.TaxIdentification,
            request.CuisineType,
            request.Email,
            request.PhoneNumber);

        if (detailsResult.IsFailure)
        {
            return detailsResult;
        }

        Result<Address> addressResult = Address.Create(
            request.Street,
            request.City,
            request.PostalCode,
            request.Country,
            request.Latitude,
            request.Longitude);

        if (addressResult.IsFailure)
        {
            return addressResult;
        }

        Result updateAddressResult = restaurant.UpdateAddress(addressResult.Value);

        if (updateAddressResult.IsFailure)
        {
            return updateAddressResult;
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}

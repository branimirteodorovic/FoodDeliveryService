using FoodDeliveryService.Common.Application.Messaging;
using FoodDeliveryService.Common.Domain;
using FoodDeliveryService.Modules.Delivery.Application.Abstractions.Data;
using FoodDeliveryService.Modules.Delivery.Domain.Restaurants;

namespace FoodDeliveryService.Modules.Delivery.Application.Restaurants.UpsertRestaurant;

internal sealed class UpsertRestaurantCommandHandler(
    IRestaurantsRepository restaurantsRepository,
    IUnitOfWork unitOfWork)
    : ICommandHandler<UpsertRestaurantCommand>
{
    public async Task<Result> Handle(UpsertRestaurantCommand request, CancellationToken cancellationToken)
    {
        Restaurant? restaurant = await restaurantsRepository.GetAsync(request.RestaurantId, cancellationToken);

        if (restaurant is null)
        {
            restaurantsRepository.Insert(
                Restaurant.Create(request.RestaurantId, request.Name, request.Latitude, request.Longitude));
        }
        else
        {
            restaurant.Update(request.Name, request.Latitude, request.Longitude);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}

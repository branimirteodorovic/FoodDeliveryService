using FoodDeliveryService.Common.Application.Messaging;
using FoodDeliveryService.Common.Domain;
using FoodDeliveryService.Modules.Orders.Application.Abstractions.Data;
using FoodDeliveryService.Modules.Orders.Domain.Restaurants;

namespace FoodDeliveryService.Modules.Orders.Application.Restaurants.UpsertRestaurant;

internal sealed class UpsertRestaurantCommandHandler(
    IRestaurantReplicaRepository restaurantRepository,
    IUnitOfWork unitOfWork)
    : ICommandHandler<UpsertRestaurantCommand>
{
    public async Task<Result> Handle(UpsertRestaurantCommand request, CancellationToken cancellationToken)
    {
        Restaurant? restaurant = await restaurantRepository.GetAsync(request.RestaurantId, cancellationToken);

        if (restaurant is null)
        {
            restaurantRepository.Insert(
                Restaurant.Create(request.RestaurantId, request.ManagerUserId, request.Name, request.CommissionRate));
        }
        else
        {
            restaurant.Update(request.ManagerUserId, request.Name, request.CommissionRate);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}

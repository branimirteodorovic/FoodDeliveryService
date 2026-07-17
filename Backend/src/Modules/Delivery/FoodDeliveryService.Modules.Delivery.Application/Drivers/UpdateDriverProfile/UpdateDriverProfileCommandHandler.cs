using FoodDeliveryService.Common.Application.Messaging;
using FoodDeliveryService.Common.Domain;
using FoodDeliveryService.Modules.Delivery.Application.Abstractions.Authentication;
using FoodDeliveryService.Modules.Delivery.Application.Abstractions.Data;
using FoodDeliveryService.Modules.Delivery.Domain.Drivers;

namespace FoodDeliveryService.Modules.Delivery.Application.Drivers.UpdateDriverProfile;

internal sealed class UpdateDriverProfileCommandHandler(
    IDriversRepository driversRepository,
    IDeliveryContext deliveryContext,
    IUnitOfWork unitOfWork)
    : ICommandHandler<UpdateDriverProfileCommand>
{
    public async Task<Result> Handle(UpdateDriverProfileCommand request, CancellationToken cancellationToken)
    {
        Driver? driver = await driversRepository.GetAsync(deliveryContext.UserId, cancellationToken);

        if (driver is null)
        {
            return Result.Failure(DriverErrors.NotOnboarded);
        }

        var vehicleType = Enum.Parse<VehicleType>(request.VehicleType, ignoreCase: true);

        Result updateResult = driver.UpdateProfile(request.FirstName, request.LastName, vehicleType);

        if (updateResult.IsFailure)
        {
            return updateResult;
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}

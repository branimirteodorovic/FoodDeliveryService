using FoodDeliveryService.Common.Application.Messaging;
using FoodDeliveryService.Common.Domain;
using FoodDeliveryService.Modules.Delivery.Application.Abstractions.Data;
using FoodDeliveryService.Modules.Delivery.Domain.Drivers;

namespace FoodDeliveryService.Modules.Delivery.Application.Drivers.SyncDriverFromUserProfile;

internal sealed class SyncDriverFromUserProfileCommandHandler(
    IDriversRepository driversRepository,
    IUnitOfWork unitOfWork)
    : ICommandHandler<SyncDriverFromUserProfileCommand>
{
    public async Task<Result> Handle(SyncDriverFromUserProfileCommand request, CancellationToken cancellationToken)
    {
        Driver? driver = await driversRepository.GetAsync(request.UserId, cancellationToken);

        if (driver is null)
        {
            // Not a driver — the profile update belongs to a customer/manager/admin.
            return Result.Success();
        }

        driver.SyncFromUserProfile(request.FirstName, request.LastName);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}

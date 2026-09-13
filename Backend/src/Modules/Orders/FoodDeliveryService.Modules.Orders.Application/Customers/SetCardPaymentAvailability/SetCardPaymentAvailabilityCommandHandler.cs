using FoodDeliveryService.Common.Application.Messaging;
using FoodDeliveryService.Common.Domain;
using FoodDeliveryService.Modules.Orders.Application.Abstractions.Data;
using FoodDeliveryService.Modules.Orders.Domain.Customers;

namespace FoodDeliveryService.Modules.Orders.Application.Customers.SetCardPaymentAvailability;

internal sealed class SetCardPaymentAvailabilityCommandHandler(
    ICustomerPaymentProfileRepository profileRepository,
    IUnitOfWork unitOfWork)
    : ICommandHandler<SetCardPaymentAvailabilityCommand>
{
    public async Task<Result> Handle(
        SetCardPaymentAvailabilityCommand request,
        CancellationToken cancellationToken)
    {
        CustomerPaymentProfile? profile = await profileRepository.GetAsync(request.CustomerId, cancellationToken);

        if (profile is null)
        {
            // Upsert, not insert: the inbox dispatches at least once, and a detach can legitimately
            // be the first event this module sees for a customer whose attach it missed.
            profileRepository.Insert(CustomerPaymentProfile.Create(
                request.CustomerId,
                request.CanPayByCard,
                request.ChangedOnUtc));
        }
        else
        {
            profile.Apply(request.CanPayByCard, request.ChangedOnUtc);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}

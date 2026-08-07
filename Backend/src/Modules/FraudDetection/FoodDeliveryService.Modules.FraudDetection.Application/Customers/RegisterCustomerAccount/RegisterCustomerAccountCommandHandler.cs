using FoodDeliveryService.Common.Application.Messaging;
using FoodDeliveryService.Common.Domain;
using FoodDeliveryService.Modules.FraudDetection.Application.Abstractions.Data;
using FoodDeliveryService.Modules.FraudDetection.Application.Behaviours;
using FoodDeliveryService.Modules.FraudDetection.Domain.Customers;

namespace FoodDeliveryService.Modules.FraudDetection.Application.Customers.RegisterCustomerAccount;

internal sealed class RegisterCustomerAccountCommandHandler(
    ICustomerBehavioursRepository customerBehaviours,
    IUnitOfWork unitOfWork)
    : ICommandHandler<RegisterCustomerAccountCommand>
{
    public async Task<Result> Handle(RegisterCustomerAccountCommand request, CancellationToken cancellationToken)
    {
        CustomerBehaviour behaviour = await customerBehaviours.GetOrCreateAsync(
            request.CustomerId,
            request.RegisteredOnUtc,
            cancellationToken);

        behaviour.Register(request.RegisteredOnUtc);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}

using FoodDeliveryService.Common.Application.Messaging;
using FoodDeliveryService.Common.Domain;
using FoodDeliveryService.Modules.Orders.Application.Abstractions.Data;
using FoodDeliveryService.Modules.Orders.Domain.Customers;

namespace FoodDeliveryService.Modules.Orders.Application.Customers.UpdateCustomer;

internal sealed class UpdateCustomerCommandHandler(
    ICustomerRepository customerRepository,
    IUnitOfWork unitOfWork)
    : ICommandHandler<UpdateCustomerCommand>
{
    public async Task<Result> Handle(UpdateCustomerCommand request, CancellationToken cancellationToken)
    {
        Customer? customer = await customerRepository.GetAsync(request.UserId, cancellationToken);

        // UserProfileUpdated fires for every actor (managers/admins included); only customers are
        // replicated here, so an unknown user is a deliberate no-op, not an error.
        if (customer is null)
        {
            return Result.Success();
        }

        customer.Update(request.FirstName, request.LastName);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}

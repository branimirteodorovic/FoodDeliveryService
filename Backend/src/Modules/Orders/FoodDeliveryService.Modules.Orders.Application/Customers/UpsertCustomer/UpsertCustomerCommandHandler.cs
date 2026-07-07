using FoodDeliveryService.Common.Application.Messaging;
using FoodDeliveryService.Common.Domain;
using FoodDeliveryService.Modules.Orders.Application.Abstractions.Data;
using FoodDeliveryService.Modules.Orders.Domain.Customers;

namespace FoodDeliveryService.Modules.Orders.Application.Customers.UpsertCustomer;

internal sealed class UpsertCustomerCommandHandler(
    ICustomerRepository customerRepository,
    IUnitOfWork unitOfWork)
    : ICommandHandler<UpsertCustomerCommand>
{
    public async Task<Result> Handle(UpsertCustomerCommand request, CancellationToken cancellationToken)
    {
        Customer? customer = await customerRepository.GetAsync(request.UserId, cancellationToken);

        if (customer is null)
        {
            customerRepository.Insert(
                Customer.Create(request.UserId, request.Email, request.FirstName, request.LastName));
        }
        else
        {
            customer.Update(request.FirstName, request.LastName);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}

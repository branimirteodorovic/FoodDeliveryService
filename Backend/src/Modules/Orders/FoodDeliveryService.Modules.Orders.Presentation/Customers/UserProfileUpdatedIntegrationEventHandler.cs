using FoodDeliveryService.Common.Application.EventBus;
using FoodDeliveryService.Common.Domain;
using FoodDeliveryService.Modules.Orders.Application.Customers.UpdateCustomer;
using FoodDeliveryService.Modules.Users.IntegrationEvents;
using MediatR;

namespace FoodDeliveryService.Modules.Orders.Presentation.Customers;

/// <summary>
/// Keeps the Customer replica's name in sync. The command no-ops for users that are not replicated
/// here (e.g. managers/admins), so every profile update can be consumed safely.
/// </summary>
internal sealed class UserProfileUpdatedIntegrationEventHandler(ISender sender)
    : IntegrationEventHandler<UserProfileUpdatedIntegrationEvent>
{
    public override async Task Handle(
        UserProfileUpdatedIntegrationEvent integrationEvent,
        CancellationToken cancellationToken = default)
    {
        Result result = await sender.Send(
            new UpdateCustomerCommand(
                integrationEvent.UserId,
                integrationEvent.FirstName,
                integrationEvent.LastName),
            cancellationToken);

        if (result.IsFailure)
        {
            throw new Common.Application.Exceptions.ApplicationException(
                nameof(UpdateCustomerCommand),
                result.Error);
        }
    }
}

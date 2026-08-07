using FoodDeliveryService.Common.Application.EventBus;
using FoodDeliveryService.Common.Domain;
using FoodDeliveryService.Modules.FraudDetection.Application.Customers.RegisterCustomerAccount;
using FoodDeliveryService.Modules.Users.IntegrationEvents;
using MediatR;

namespace FoodDeliveryService.Modules.FraudDetection.Presentation.Users;

/// <summary>
/// Records account creation on the customer behaviour projection. The event's OccurredOnUtc is the
/// registration moment — Users publishes it from the registration itself.
/// </summary>
internal sealed class UserRegisteredIntegrationEventHandler(ISender sender)
    : IntegrationEventHandler<UserRegisteredIntegrationEvent>
{
    public override async Task Handle(
        UserRegisteredIntegrationEvent integrationEvent,
        CancellationToken cancellationToken = default)
    {
        Result result = await sender.Send(
            new RegisterCustomerAccountCommand(integrationEvent.UserId, integrationEvent.OccurredOnUtc),
            cancellationToken);

        if (result.IsFailure)
        {
            throw new Common.Application.Exceptions.ApplicationException(
                nameof(RegisterCustomerAccountCommand),
                result.Error);
        }
    }
}

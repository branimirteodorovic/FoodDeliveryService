using FoodDeliveryService.Common.Application.EventBus;
using FoodDeliveryService.Common.Domain;
using FoodDeliveryService.Modules.Payments.Application.PaymentMethods.CreateCustomerProfile;
using FoodDeliveryService.Modules.Users.IntegrationEvents;
using MediatR;

namespace FoodDeliveryService.Modules.Payments.Presentation.Users;

/// <summary>
/// Creates the Stripe customer for every registered user — §6.2.
/// <para>
/// <b>Every</b> user, not only the ones with the Customer role. A driver or a restaurant manager can
/// place an order like anyone else, and role-filtering here (the way Support's agent replica does)
/// would mean the first card they try to save fails with a profile that does not exist. A Stripe
/// customer with no payment method costs nothing and is not a person's data — it is an empty
/// container holding an id.
/// </para>
/// </summary>
internal sealed class UserRegisteredIntegrationEventHandler(ISender sender)
    : IntegrationEventHandler<UserRegisteredIntegrationEvent>
{
    public override async Task Handle(
        UserRegisteredIntegrationEvent integrationEvent,
        CancellationToken cancellationToken = default)
    {
        Result result = await sender.Send(
            new CreateCustomerProfileCommand(
                integrationEvent.UserId,
                integrationEvent.Email,
                integrationEvent.FirstName,
                integrationEvent.LastName),
            cancellationToken);

        if (result.IsFailure)
        {
            throw new Common.Application.Exceptions.ApplicationException(
                nameof(CreateCustomerProfileCommand),
                result.Error);
        }
    }
}

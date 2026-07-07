using FoodDeliveryService.Common.Application.EventBus;
using FoodDeliveryService.Common.Domain;
using FoodDeliveryService.Modules.Orders.Application.Customers.UpsertCustomer;
using FoodDeliveryService.Modules.Users.IntegrationEvents;
using MediatR;

namespace FoodDeliveryService.Modules.Orders.Presentation.Customers;

/// <summary>
/// Maintains the local Customer replica (dispatched by ProcessInboxJob, idempotent via the inbox).
/// Fires for every registration — managers/admins included — so non-customer users are skipped; only
/// Customer accounts are replicated here.
/// </summary>
internal sealed class UserRegisteredIntegrationEventHandler(ISender sender)
    : IntegrationEventHandler<UserRegisteredIntegrationEvent>
{
    // Role name as seeded by the Users service (Users.Domain Role.Customer) — carried in the event's
    // role snapshot; the Users domain itself is not referenced (hard rule #4).
    private const string CustomerRole = "Customer";

    public override async Task Handle(
        UserRegisteredIntegrationEvent integrationEvent,
        CancellationToken cancellationToken = default)
    {
        if (!integrationEvent.Roles.Contains(CustomerRole))
        {
            return;
        }

        Result result = await sender.Send(
            new UpsertCustomerCommand(
                integrationEvent.UserId,
                integrationEvent.Email,
                integrationEvent.FirstName,
                integrationEvent.LastName),
            cancellationToken);

        if (result.IsFailure)
        {
            throw new Common.Application.Exceptions.ApplicationException(
                nameof(UpsertCustomerCommand),
                result.Error);
        }
    }
}

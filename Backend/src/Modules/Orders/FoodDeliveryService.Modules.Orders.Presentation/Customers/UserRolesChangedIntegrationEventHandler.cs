using FoodDeliveryService.Common.Application.EventBus;
using FoodDeliveryService.Common.Domain;
using FoodDeliveryService.Modules.Orders.Application.Customers.UpsertCustomer;
using FoodDeliveryService.Modules.Users.IntegrationEvents;
using MediatR;

namespace FoodDeliveryService.Modules.Orders.Presentation.Customers;

/// <summary>
/// Back-fills the Customer replica when a role change grants the Customer role to an account that
/// did not have it at registration — a driver or a manager who is now also allowed to order.
/// <para>
/// This exists because <see cref="UserRegisteredIntegrationEventHandler"/> is role-filtered and runs
/// exactly once, at registration. <c>PlaceOrderCommandHandler</c> hard-requires a Customer row, so
/// without this the grant would hand somebody <c>orders:create</c> and a 404 on the first order they
/// place. Same idempotent upsert command, so a replay is free.
/// </para>
/// <para>
/// The revoke direction is deliberately not handled: the replica row carries the name on every order
/// this person already placed, and authorization for a new one is resolved from Users per request,
/// not from this table.
/// </para>
/// </summary>
internal sealed class UserRolesChangedIntegrationEventHandler(ISender sender)
    : IntegrationEventHandler<UserRolesChangedIntegrationEvent>
{
    // Role name as seeded by the Users service (Users.Domain Role.Customer) — carried in the event's
    // role snapshot; the Users domain itself is not referenced (hard rule #4).
    private const string CustomerRole = "Customer";

    public override async Task Handle(
        UserRolesChangedIntegrationEvent integrationEvent,
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

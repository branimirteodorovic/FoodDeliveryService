using FoodDeliveryService.Common.Application.Messaging;

namespace FoodDeliveryService.Modules.Orders.Application.Customers.UpsertCustomer;

// Builds the local Customer replica from UserRegisteredIntegrationEvent (inbox-driven, idempotent —
// hence upsert semantics).
public sealed record UpsertCustomerCommand(
    Guid UserId,
    string Email,
    string FirstName,
    string LastName) : ICommand;

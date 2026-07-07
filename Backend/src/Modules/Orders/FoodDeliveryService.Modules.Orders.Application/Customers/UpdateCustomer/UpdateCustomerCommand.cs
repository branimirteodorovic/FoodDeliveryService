using FoodDeliveryService.Common.Application.Messaging;

namespace FoodDeliveryService.Modules.Orders.Application.Customers.UpdateCustomer;

// Syncs the Customer replica's name from UserProfileUpdatedIntegrationEvent. No-ops for users not
// replicated here (e.g. managers/admins), so every profile update can be consumed safely.
public sealed record UpdateCustomerCommand(
    Guid UserId,
    string FirstName,
    string LastName) : ICommand;

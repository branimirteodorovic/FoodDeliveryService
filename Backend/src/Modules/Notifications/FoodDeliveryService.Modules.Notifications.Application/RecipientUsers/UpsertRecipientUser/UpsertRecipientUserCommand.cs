using FoodDeliveryService.Common.Application.Messaging;

namespace FoodDeliveryService.Modules.Notifications.Application.RecipientUsers.UpsertRecipientUser;

// Builds the local RecipientUser replica from UserRegisteredIntegrationEvent (inbox-driven,
// idempotent — hence upsert semantics). Keeps every role so Phase-2 real-time/push can resolve
// managers/drivers too.
public sealed record UpsertRecipientUserCommand(
    Guid UserId,
    string Email,
    string FirstName,
    string LastName) : ICommand;

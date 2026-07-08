using FoodDeliveryService.Common.Application.Messaging;

namespace FoodDeliveryService.Modules.Notifications.Application.RecipientUsers.UpdateRecipientUser;

// Syncs the RecipientUser replica's name from UserProfileUpdatedIntegrationEvent. No-ops for a user
// not yet replicated here (the register event may still be in flight), so every profile update can
// be consumed safely.
public sealed record UpdateRecipientUserCommand(
    Guid UserId,
    string FirstName,
    string LastName) : ICommand;

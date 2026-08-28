using FoodDeliveryService.Common.Application.Messaging;

namespace FoodDeliveryService.Modules.Support.Application.Agents.UpdateSupportAgent;

// Keeps the replica's name in sync from UserProfileUpdatedIntegrationEvent. Every user's profile
// update reaches this module, so it no-ops for the overwhelming majority who are not agents.
public sealed record UpdateSupportAgentCommand(Guid UserId, string FirstName, string LastName) : ICommand;

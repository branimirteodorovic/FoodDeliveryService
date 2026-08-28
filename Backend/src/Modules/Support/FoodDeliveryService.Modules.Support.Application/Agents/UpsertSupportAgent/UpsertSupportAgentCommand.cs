using FoodDeliveryService.Common.Application.Messaging;

namespace FoodDeliveryService.Modules.Support.Application.Agents.UpsertSupportAgent;

// Builds the local SupportAgentReplica from UserRegisteredIntegrationEvent. Inbox-driven, so upsert
// semantics rather than insert: a redelivered registration must not fail on a duplicate key.
public sealed record UpsertSupportAgentCommand(
    Guid UserId,
    string Email,
    string FirstName,
    string LastName) : ICommand;

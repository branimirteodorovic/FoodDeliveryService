using FoodDeliveryService.Common.Application.Messaging;

namespace FoodDeliveryService.Modules.Users.Application.Users.DeactivateUser;

/// <summary>
/// Removes a provisioned-but-never-activated account — the compensation the Restaurants module
/// triggers when onboarding fails after the manager was provisioned (via
/// DeactivateProvisionedUserRequest). Refused for activated accounts (Identity guards the flag).
/// </summary>
public sealed record DeactivateUserCommand(Guid UserId) : ICommand;

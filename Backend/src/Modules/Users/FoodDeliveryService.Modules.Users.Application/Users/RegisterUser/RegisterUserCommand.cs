using FoodDeliveryService.Common.Application.Messaging;

namespace FoodDeliveryService.Modules.Users.Application.Users.RegisterUser;

/// <summary>
/// Registers a user. Two modes:
/// <list type="bullet">
/// <item>Self-service (default): <c>Role = "Customer"</c>, <c>RequireInvitation = false</c> — a
/// caller-supplied <see cref="Password"/> is used.</item>
/// <item>Admin-provisioned: <c>RequireInvitation = true</c> (e.g. Role = "RestaurantManager") — the
/// identity is created with no password and an activation token is emailed; <see cref="Password"/>
/// is ignored/absent.</item>
/// </list>
/// </summary>
public sealed record RegisterUserCommand(
    string Email,
    string Password,
    string FirstName,
    string LastName,
    string Role = "Customer",
    bool RequireInvitation = false) : ICommand<Guid>;

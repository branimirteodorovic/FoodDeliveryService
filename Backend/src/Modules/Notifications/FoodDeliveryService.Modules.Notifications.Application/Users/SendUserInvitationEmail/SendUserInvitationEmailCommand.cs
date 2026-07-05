using FoodDeliveryService.Common.Application.Messaging;

namespace FoodDeliveryService.Modules.Notifications.Application.Users.SendUserInvitationEmail;

/// <summary>
/// Sends the invitation email for an invited account. Carries the one-time activation token (never a
/// password); the email service composes the activation link.
/// </summary>
public sealed record SendUserInvitationEmailCommand(
    string Email,
    string FirstName,
    string ActivationToken,
    DateTime ExpiresOnUtc) : ICommand;

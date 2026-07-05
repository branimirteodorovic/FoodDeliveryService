using FoodDeliveryService.Common.Application.Messaging;

namespace FoodDeliveryService.Modules.Users.Application.Users.AcceptInvitation;

/// <summary>
/// Activates an invited account: the invitee supplies the emailed one-time token and their chosen
/// password. Anonymous — the account has no usable credentials until this succeeds.
/// </summary>
public sealed record AcceptInvitationCommand(string Email, string Token, string NewPassword) : ICommand;

using FoodDeliveryService.Common.Domain;

namespace FoodDeliveryService.Modules.Users.Application.Abstractions.Identity;

public interface IIdentityProviderService
{
    Task<Result<string>> RegisterUserAsync(UserModel user, CancellationToken cancellationToken = default);

    /// <summary>
    /// Provisions an invited account with no usable password and returns its identity id plus a
    /// one-time activation token the invitee uses to set a password (see <see cref="SetPasswordAsync"/>).
    /// </summary>
    Task<Result<InvitationResult>> RegisterInvitedUserAsync(
        InvitedUserModel user,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Consumes an activation token: validates it, sets the invitee's chosen password and activates
    /// the account. Invalid/expired tokens fail.
    /// </summary>
    Task<Result> SetPasswordAsync(
        string email,
        string token,
        string newPassword,
        CancellationToken cancellationToken = default);
}

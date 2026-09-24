using FoodDeliveryService.Common.Domain;

namespace FoodDeliveryService.Modules.Users.Domain.Users;

public static class UserErrors
{
    public static Error NotFound(Guid userId) =>
        Error.NotFound("Users.NotFound", $"The user with the identifier {userId} not found");

    public static Error NotFound(string identityId) =>
        Error.NotFound("Users.NotFound", $"The user with the IDP identifier {identityId} not found");

    /// <summary>
    /// A user with no roles has no permissions at all, which is not "a restricted account" but a
    /// broken one: <c>GetUserPermissionsQuery</c> answers NotFound for an empty join, so every
    /// service would report them as unknown rather than as forbidden.
    /// </summary>
    public static readonly Error AtLeastOneRoleRequired =
        Error.Problem("Users.AtLeastOneRoleRequired", "A user must hold at least one role.");

    /// <summary>
    /// Covers both an unknown role name and <c>Administrator</c>, which is deliberately absent from
    /// <see cref="Role.Assignable"/> — the initial administrator is seeded from configuration and
    /// nothing on the platform can mint another one.
    /// </summary>
    public static Error RoleNotAssignable(string roleName) =>
        Error.Problem(
            "Users.RoleNotAssignable",
            $"'{roleName}' is not a role that can be assigned. Assignable roles are: " +
            $"{string.Join(", ", Role.Assignable.Select(role => role.Name))}.");

    /// <summary>
    /// An administrator's own roles are out of this operation's reach entirely. Since the
    /// replacement set can only contain assignable roles, and Administrator is not one, any change
    /// applied to an administrator would silently strip the role — up to and including the last
    /// administrator on the platform locking everyone out of this very endpoint.
    /// </summary>
    public static readonly Error AdministratorRolesAreImmutable =
        Error.Conflict(
            "Users.AdministratorRolesAreImmutable",
            "An administrator's roles cannot be changed. The Administrator role is granted only by " +
            "the configured seed, and a role change would strip it.");
}

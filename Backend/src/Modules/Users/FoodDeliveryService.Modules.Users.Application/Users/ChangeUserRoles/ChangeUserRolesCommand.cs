using FoodDeliveryService.Common.Application.Messaging;

namespace FoodDeliveryService.Modules.Users.Application.Users.ChangeUserRoles;

/// <summary>
/// Replaces a user's roles with <paramref name="Roles"/> — administrator only. A replacement rather
/// than a grant or a revoke: the role set is what the permission join reads, so one call produces
/// one resulting set, and there is no ordering question between two half-applied changes.
/// <para>
/// <see cref="Roles"/> are role <em>names</em> as the Users module seeds them ("Customer",
/// "SupportAgent", …). <c>Administrator</c> is not among them — see
/// <c>Role.Assignable</c>.
/// </para>
/// </summary>
public sealed record ChangeUserRolesCommand(Guid UserId, IReadOnlyCollection<string> Roles) : ICommand;

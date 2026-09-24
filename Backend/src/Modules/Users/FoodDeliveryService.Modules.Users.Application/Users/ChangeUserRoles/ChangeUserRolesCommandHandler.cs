using FoodDeliveryService.Common.Application.Caching;
using FoodDeliveryService.Common.Application.Messaging;
using FoodDeliveryService.Common.Domain;
using FoodDeliveryService.Modules.Users.Application.Abstractions.Data;
using FoodDeliveryService.Modules.Users.Domain.Users;

namespace FoodDeliveryService.Modules.Users.Application.Users.ChangeUserRoles;

internal sealed class ChangeUserRolesCommandHandler(
    IUserRepository userRepository,
    IUnitOfWork unitOfWork,
    ICacheService cacheService)
    : ICommandHandler<ChangeUserRolesCommand>
{
    public async Task<Result> Handle(ChangeUserRolesCommand request, CancellationToken cancellationToken)
    {
        // With roles: ChangeRoles compares against and replaces the current set, and a set EF never
        // loaded would look empty — every requested role would be inserted and none removed.
        User? user = await userRepository.GetWithRolesAsync(request.UserId, cancellationToken);

        if (user is null)
        {
            return Result.Failure(UserErrors.NotFound(request.UserId));
        }

        string[] requested = request.Roles.Distinct(StringComparer.Ordinal).ToArray();

        // Names are checked against the domain catalogue before the database is asked for them.
        // GetRolesAsync simply omits a name that matches no row, so without this an unknown role
        // would be silently dropped from the set rather than refused — and a request naming one
        // real role and one typo would half-apply.
        string? unknown = requested.FirstOrDefault(name => Role.FromName(name) is null);

        if (unknown is not null)
        {
            return Result.Failure(UserErrors.RoleNotAssignable(unknown));
        }

        IReadOnlyCollection<Role> roles = await userRepository.GetRolesAsync(requested, cancellationToken);

        if (roles.Count != requested.Length)
        {
            // Reachable only if the seeded roles table and Role.Assignable have diverged — a
            // migration that added a role to the catalogue but not to the database. Reported as the
            // same refusal rather than thrown, because the caller's request is genuinely
            // unsatisfiable either way.
            string missing = requested.First(name => !roles.Any(role => role.Name == name));

            return Result.Failure(UserErrors.RoleNotAssignable(missing));
        }

        Result result = user.ChangeRoles(roles);

        if (result.IsFailure)
        {
            return result;
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        // Inline, right after the save — the invalidation model in docs/caching.md §2, and the one
        // point of this command that reaches beyond this service. `user_permissions:{identityId}`
        // is a single entry shared by all eight services (no InstanceName, one Redis per
        // environment), so this one DEL is what makes the new role set authoritative everywhere on
        // the next request instead of up to five minutes later.
        //
        // Not conditional on the change being real: ChangeRoles answers Success for an unchanged
        // set too, and an eviction that was not needed costs one round trip, while one that was
        // skipped costs five minutes of a revoked permission still working. The TTL remains the
        // safety net for a crash between the save and this line.
        await cacheService.RemoveAsync(CacheKeys.UserPermissions(user.IdentityId), cancellationToken);

        return Result.Success();
    }
}

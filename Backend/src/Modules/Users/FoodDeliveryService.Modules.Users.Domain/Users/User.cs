using FoodDeliveryService.Common.Domain;
using FoodDeliveryService.Modules.Users.Domain.Users;

namespace FoodDeliveryService.Modules.Users.Domain.Users;

public sealed class User : Entity
{
    private readonly List<Role> _roles = [];

    private User()
    {
    }

    public Guid Id { get; private set; }

    public string Email { get; private set; }

    public string FirstName { get; private set; }

    public string LastName { get; private set; }

    public string IdentityId { get; private set; }

    public IReadOnlyCollection<Role> Roles => _roles.ToList();

    public static User Create(string email, string firstName, string lastName, string identityId) =>
        Create(email, firstName, lastName, identityId, Role.Customer);

    public static User Create(string email, string firstName, string lastName, string identityId, Role role) =>
        CreateInternal(email, firstName, lastName, identityId, role);

    /// <summary>
    /// Creates an admin-provisioned account activated by email invitation (no password yet). Beyond
    /// the usual <see cref="UserRegisteredDomainEvent"/>, it raises a <see cref="UserInvitedDomainEvent"/>
    /// carrying the identity provider's one-time activation token so Notifications can email the link.
    /// </summary>
    public static User CreateInvited(
        string email,
        string firstName,
        string lastName,
        string identityId,
        Role role,
        string activationToken,
        DateTime expiresOnUtc)
    {
        User user = CreateInternal(email, firstName, lastName, identityId, role);

        user.Raise(new UserInvitedDomainEvent(
            user.Id,
            user.Email,
            user.FirstName,
            user.LastName,
            activationToken,
            expiresOnUtc));

        return user;
    }

    private static User CreateInternal(string email, string firstName, string lastName, string identityId, Role role)
    {
        var user = new User
        {
            Id = Guid.NewGuid(),
            Email = email,
            FirstName = firstName,
            LastName = lastName,
            IdentityId = identityId,
        };

        user._roles.Add(role);

        user.Raise(new UserRegisteredDomainEvent(user.Id, [role.Name]));

        return user;
    }

    /// <summary>
    /// Replaces the user's role set wholesale — the administrator-only counterpart of assigning a
    /// role at creation, and the only way a role changes after an account exists.
    /// <para>
    /// It is a replacement rather than a grant/revoke pair because the role set is what the
    /// permission join reads: one call, one resulting set, one event carrying that set as a full
    /// snapshot. Callers pass roles resolved from the database (see
    /// <c>IUserRepository.GetRolesAsync</c>) so EF tracks one instance per row; every comparison
    /// here is therefore by <see cref="Role.Name"/>, never by reference — the <see cref="Role"/>
    /// statics and the rows EF materializes are different objects with the same key.
    /// </para>
    /// <para>
    /// No-ops on an unchanged set, exactly as <see cref="Update"/> does: no event, no outbox row,
    /// and nothing downstream is told a change happened that did not.
    /// </para>
    /// </summary>
    public Result ChangeRoles(IReadOnlyCollection<Role> roles)
    {
        string[] requested = roles.Select(role => role.Name).Distinct(StringComparer.Ordinal).ToArray();

        if (requested.Length == 0)
        {
            return Result.Failure(UserErrors.AtLeastOneRoleRequired);
        }

        // Role.FromName resolves against Role.Assignable, so this rejects an unknown name and
        // Administrator with the same check — which is the point: from here they are the same
        // thing, a name that may not be assigned.
        string? notAssignable = requested.FirstOrDefault(name => Role.FromName(name) is null);

        if (notAssignable is not null)
        {
            return Result.Failure(UserErrors.RoleNotAssignable(notAssignable));
        }

        string[] current = _roles.Select(role => role.Name).ToArray();

        if (current.Contains(Role.Administrator.Name, StringComparer.Ordinal))
        {
            return Result.Failure(UserErrors.AdministratorRolesAreImmutable);
        }

        if (current.Length == requested.Length &&
            requested.All(name => current.Contains(name, StringComparer.Ordinal)))
        {
            return Result.Success();
        }

        _roles.Clear();
        _roles.AddRange(roles.DistinctBy(role => role.Name, StringComparer.Ordinal));

        Raise(new UserRolesChangedDomainEvent(Id, requested));

        return Result.Success();
    }

    public void Update(string firstName, string lastName)
    {
        if (FirstName == firstName && LastName == lastName)
        {
            return;
        }

        FirstName = firstName;
        LastName = lastName;

        Raise(new UserProfileUpdatedDomainEvent(Id, FirstName, LastName));
    }
}

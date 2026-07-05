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

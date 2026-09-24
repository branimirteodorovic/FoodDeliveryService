namespace FoodDeliveryService.Modules.Users.Domain.Users;

public interface IUserRepository
{
    Task<User?> GetAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// The same user with <see cref="User.Roles"/> populated. A separate method rather than an
    /// eager-load on <see cref="GetAsync"/> because only the role-changing path needs the join, and
    /// every other caller would pay for it — but a caller that mutates the set without it would
    /// clear a collection EF never loaded, deleting nothing and adding everything.
    /// </summary>
    Task<User?> GetWithRolesAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// The <see cref="Role"/> rows for the given names, as EF tracks them. Roles are a fixed seeded
    /// set, so this never creates one — it exists so a role assigned to a user is the *tracked*
    /// instance: handing the aggregate a <see cref="Role"/> static instead makes EF try to insert a
    /// second row for a key it already tracks. Names that match no row are simply absent from the
    /// result, which is how the caller learns they were not real.
    /// </summary>
    Task<IReadOnlyCollection<Role>> GetRolesAsync(
        IReadOnlyCollection<string> names,
        CancellationToken cancellationToken = default);

    void Insert(User user);

    // Hard-delete — only used to compensate a failed onboarding (never-activated invited account).
    void Remove(User user);
}

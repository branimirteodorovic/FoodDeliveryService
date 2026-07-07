namespace FoodDeliveryService.Modules.Users.Domain.Users;

public interface IUserRepository
{
    Task<User?> GetAsync(Guid id, CancellationToken cancellationToken = default);

    void Insert(User user);

    // Hard-delete — only used to compensate a failed onboarding (never-activated invited account).
    void Remove(User user);
}

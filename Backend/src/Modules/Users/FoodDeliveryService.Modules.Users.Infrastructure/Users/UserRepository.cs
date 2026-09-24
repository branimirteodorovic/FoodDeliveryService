using FoodDeliveryService.Modules.Users.Domain.Users;
using FoodDeliveryService.Modules.Users.Infrastructure.Database;
using Microsoft.EntityFrameworkCore;

namespace FoodDeliveryService.Modules.Users.Infrastructure.Users;

internal sealed class UserRepository(UsersDbContext context) : IUserRepository
{
    public async Task<User?> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await context.Users.SingleOrDefaultAsync(u => u.Id == id, cancellationToken);
    }

    public async Task<User?> GetWithRolesAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await context.Users
            .Include(u => u.Roles)
            .SingleOrDefaultAsync(u => u.Id == id, cancellationToken);
    }

    public async Task<IReadOnlyCollection<Role>> GetRolesAsync(
        IReadOnlyCollection<string> names,
        CancellationToken cancellationToken = default)
    {
        return await context.Set<Role>()
            .Where(role => names.Contains(role.Name))
            .ToListAsync(cancellationToken);
    }

    public void Insert(User user)
    {
        foreach (Role role in user.Roles)
        {
            context.Attach(role);
        }

        context.Users.Add(user);
    }

    public void Remove(User user)
    {
        context.Users.Remove(user);
    }
}

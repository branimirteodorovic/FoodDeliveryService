using FoodDeliveryService.Modules.Notifications.Domain.RecipientUsers;
using FoodDeliveryService.Modules.Notifications.Infrastructure.Database;
using Microsoft.EntityFrameworkCore;

namespace FoodDeliveryService.Modules.Notifications.Infrastructure.RecipientUsers;

internal sealed class RecipientUsersRepository(NotificationsDbContext context) : IRecipientUserRepository
{
    public async Task<RecipientUser?> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await context.RecipientUsers.SingleOrDefaultAsync(r => r.Id == id, cancellationToken);
    }

    public void Insert(RecipientUser recipientUser)
    {
        context.RecipientUsers.Add(recipientUser);
    }
}

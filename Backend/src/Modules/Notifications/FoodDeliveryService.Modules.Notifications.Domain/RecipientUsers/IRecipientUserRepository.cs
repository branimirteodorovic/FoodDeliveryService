namespace FoodDeliveryService.Modules.Notifications.Domain.RecipientUsers;

public interface IRecipientUserRepository
{
    Task<RecipientUser?> GetAsync(Guid id, CancellationToken cancellationToken = default);

    void Insert(RecipientUser recipientUser);
}

using FoodDeliveryService.Common.Domain;

namespace FoodDeliveryService.Modules.Notifications.Domain.RecipientUsers;

/// <summary>
/// Local read-only replica of a user, keyed by the Users service's UserId and populated
/// asynchronously from UserRegistered/UserProfileUpdated integration events. Lets Notifications
/// resolve a notification recipient (userId → email, name) without querying the Users database
/// (hard rule #5). Unlike the Orders Customer replica this keeps every role, since Phase-2
/// real-time/push must address managers/drivers too. As a projection of state owned by another
/// service it raises no domain events — the Users service already published the originating events.
/// </summary>
public sealed class RecipientUser : Entity
{
    private RecipientUser()
    {
    }

    public Guid Id { get; private set; }

    public string Email { get; private set; }

    public string FirstName { get; private set; }

    public string LastName { get; private set; }

    public static RecipientUser Create(Guid userId, string email, string firstName, string lastName)
    {
        return new RecipientUser
        {
            Id = userId,
            Email = email,
            FirstName = firstName,
            LastName = lastName
        };
    }

    public void Update(string firstName, string lastName)
    {
        FirstName = firstName;
        LastName = lastName;
    }
}

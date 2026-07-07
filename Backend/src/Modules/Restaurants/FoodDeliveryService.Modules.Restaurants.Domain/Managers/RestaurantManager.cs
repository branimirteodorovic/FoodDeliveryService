using FoodDeliveryService.Common.Domain;

namespace FoodDeliveryService.Modules.Restaurants.Domain.Managers;

/// <summary>
/// Local read-only replica of a RestaurantManager user, keyed by the Users service's UserId and
/// populated asynchronously from UserRegistered/UserProfileUpdated integration events (same pattern
/// Orders uses for customers). Used to attribute/display the manager without querying the Users
/// database. As a projection of state owned by another service it raises no domain events — the
/// Users service already published the originating events.
/// </summary>
public sealed class RestaurantManager : Entity
{
    private RestaurantManager()
    {
    }

    public Guid Id { get; private set; }

    public string Email { get; private set; }

    public string FirstName { get; private set; }

    public string LastName { get; private set; }

    public static RestaurantManager Create(Guid userId, string email, string firstName, string lastName)
    {
        return new RestaurantManager
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

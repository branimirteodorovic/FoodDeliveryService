using FoodDeliveryService.Common.Domain;

namespace FoodDeliveryService.Modules.Orders.Domain.Customers;

/// <summary>
/// Local read-only replica of a customer, keyed by the Users service's UserId and populated
/// asynchronously from UserRegistered/UserProfileUpdated integration events. Lets Orders attribute
/// and price an order without querying the Users database (hard rule #5). As a projection of state
/// owned by another service it raises no domain events — the Users service already published the
/// originating events.
/// </summary>
public sealed class Customer : Entity
{
    private Customer()
    {
    }

    public Guid Id { get; private set; }

    public string Email { get; private set; }

    public string FirstName { get; private set; }

    public string LastName { get; private set; }

    public static Customer Create(Guid userId, string email, string firstName, string lastName)
    {
        return new Customer
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

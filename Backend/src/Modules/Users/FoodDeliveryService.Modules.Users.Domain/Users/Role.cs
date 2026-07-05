namespace FoodDeliveryService.Modules.Users.Domain.Users;

public sealed class Role
{
    public static readonly Role Administrator = new("Administrator");
    public static readonly Role Customer = new("Customer");                 // was Member — the only self-registering actor
    public static readonly Role RestaurantManager = new("RestaurantManager");
    // Later iterations: DeliveryDriver, SupportAgent

    private Role(string name)
    {
        Name = name;
    }

    private Role()
    {
    }

    public string Name { get; private set; }
}

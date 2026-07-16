namespace FoodDeliveryService.Modules.Users.Domain.Users;

public sealed class Role
{
    public static readonly Role Administrator = new("Administrator");
    public static readonly Role Customer = new("Customer");                 // was Member — the only self-registering actor
    public static readonly Role RestaurantManager = new("RestaurantManager");
    public static readonly Role DeliveryDriver = new("DeliveryDriver");      // admin-provisioned, never self-registered
    // Later iterations: SupportAgent

    // Roles assignable at account creation. Administrator is intentionally excluded — no one can
    // register or be provisioned as an admin (the initial admin is seeded from configuration).
    public static readonly IReadOnlyCollection<Role> Assignable = [Customer, RestaurantManager, DeliveryDriver];

    private Role(string name)
    {
        Name = name;
    }

    private Role()
    {
    }

    public string Name { get; private set; }

    public static Role? FromName(string name) =>
        Assignable.SingleOrDefault(role => role.Name == name);
}

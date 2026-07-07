using FoodDeliveryService.Common.Domain;

namespace FoodDeliveryService.Modules.Restaurants.Domain.Restaurants;

/// <summary>
/// Child of the <see cref="Restaurant"/> aggregate ("Starters", "Mains", …). All mutations are
/// internal — they go through the aggregate root, which enforces invariants (duplicate names) and
/// raises the domain events.
/// </summary>
public sealed class MenuCategory : Entity
{
    private readonly List<MenuItem> _items = [];

    private MenuCategory()
    {
    }

    public Guid Id { get; private set; }

    public Guid RestaurantId { get; private set; }

    public string Name { get; private set; }

    public int DisplayOrder { get; private set; }

    public IReadOnlyCollection<MenuItem> Items => _items.ToList();

    internal static MenuCategory Create(Guid restaurantId, string name, int displayOrder)
    {
        return new MenuCategory
        {
            Id = Guid.NewGuid(),
            RestaurantId = restaurantId,
            Name = name,
            DisplayOrder = displayOrder
        };
    }

    internal void Update(string name, int displayOrder)
    {
        Name = name;
        DisplayOrder = displayOrder;
    }

    internal void AddItem(MenuItem item)
    {
        _items.Add(item);
    }
}

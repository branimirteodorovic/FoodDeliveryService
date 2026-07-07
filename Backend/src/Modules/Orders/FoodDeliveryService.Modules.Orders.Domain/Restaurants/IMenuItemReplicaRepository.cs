namespace FoodDeliveryService.Modules.Orders.Domain.Restaurants;

public interface IMenuItemReplicaRepository
{
    Task<MenuItem?> GetAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Batch lookup for placement: all requested items of one restaurant in a single round trip.
    /// Items that do not exist (or belong to another restaurant) are simply absent from the result —
    /// the placement handler treats missing ids as unknown items.
    /// </summary>
    Task<IReadOnlyCollection<MenuItem>> GetManyAsync(
        Guid restaurantId,
        IReadOnlyCollection<Guid> menuItemIds,
        CancellationToken cancellationToken = default);

    void Insert(MenuItem menuItem);
}

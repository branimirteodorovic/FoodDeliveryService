namespace FoodDeliveryService.Modules.RealTime.Infrastructure.Database.RestaurantManagers;

/// <summary>
/// The service's only durable replica row (Milestone D): <c>Id</c> is the manager's module-side user
/// id (never generated locally), mapped to the restaurant they manage. Plain EF-mapped POCO, not a
/// DDD aggregate — this module has no Domain project, raises no domain events and enforces no
/// invariants beyond "the current snapshot from Restaurants' events".
/// </summary>
internal sealed class RestaurantManager
{
    private RestaurantManager()
    {
    }

    public Guid Id { get; private set; }

    public Guid RestaurantId { get; private set; }

    public string RestaurantName { get; private set; } = string.Empty;

    public static RestaurantManager Create(Guid managerUserId, Guid restaurantId, string restaurantName) =>
        new()
        {
            Id = managerUserId,
            RestaurantId = restaurantId,
            RestaurantName = restaurantName
        };

    public void UpdateRestaurant(Guid restaurantId, string restaurantName)
    {
        RestaurantId = restaurantId;
        RestaurantName = restaurantName;
    }

    public void RenameRestaurant(string restaurantName)
    {
        RestaurantName = restaurantName;
    }
}

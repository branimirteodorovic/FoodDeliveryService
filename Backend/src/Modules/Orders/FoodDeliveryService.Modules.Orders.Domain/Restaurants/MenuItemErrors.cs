using FoodDeliveryService.Common.Domain;

namespace FoodDeliveryService.Modules.Orders.Domain.Restaurants;

public static class MenuItemErrors
{
    // Raised when an availability change arrives before the item's Added event (separate queues can
    // deliver out of order); the throwing handler makes the inbox retry until the replica exists.
    public static Error NotFound(Guid menuItemId) =>
        Error.NotFound(
            "Orders.MenuItemNotFound",
            $"The menu item replica with the identifier {menuItemId} was not found");
}

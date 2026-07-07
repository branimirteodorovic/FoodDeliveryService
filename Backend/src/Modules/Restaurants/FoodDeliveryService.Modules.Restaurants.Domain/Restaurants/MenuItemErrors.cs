using FoodDeliveryService.Common.Domain;

namespace FoodDeliveryService.Modules.Restaurants.Domain.Restaurants;

public static class MenuItemErrors
{
    public static Error NotFound(Guid menuItemId) =>
        Error.NotFound(
            "MenuItems.NotFound",
            $"The menu item with the identifier {menuItemId} was not found");

    public static readonly Error InvalidPrice = Error.Problem(
        "MenuItems.InvalidPrice",
        "The menu item price must be greater than zero");
}

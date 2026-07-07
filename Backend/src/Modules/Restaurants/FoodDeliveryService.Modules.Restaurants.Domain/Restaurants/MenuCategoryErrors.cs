using FoodDeliveryService.Common.Domain;

namespace FoodDeliveryService.Modules.Restaurants.Domain.Restaurants;

public static class MenuCategoryErrors
{
    public static Error NotFound(Guid categoryId) =>
        Error.NotFound(
            "MenuCategories.NotFound",
            $"The menu category with the identifier {categoryId} was not found");

    public static Error DuplicateName(string name) =>
        Error.Conflict(
            "MenuCategories.DuplicateName",
            $"The restaurant already has a menu category named '{name}'");
}

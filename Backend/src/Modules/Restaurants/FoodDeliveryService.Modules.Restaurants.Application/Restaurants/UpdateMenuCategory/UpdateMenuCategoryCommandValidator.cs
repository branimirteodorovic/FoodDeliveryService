using FluentValidation;

namespace FoodDeliveryService.Modules.Restaurants.Application.Restaurants.UpdateMenuCategory;

internal sealed class UpdateMenuCategoryCommandValidator : AbstractValidator<UpdateMenuCategoryCommand>
{
    public UpdateMenuCategoryCommandValidator()
    {
        RuleFor(c => c.RestaurantId).NotEqual(Guid.Empty);
        RuleFor(c => c.CategoryId).NotEqual(Guid.Empty);
        RuleFor(c => c.Name).NotEmpty().MaximumLength(200);
        RuleFor(c => c.DisplayOrder).GreaterThanOrEqualTo(0);
    }
}

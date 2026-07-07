using FluentValidation;

namespace FoodDeliveryService.Modules.Restaurants.Application.Restaurants.CreateMenuCategory;

internal sealed class CreateMenuCategoryCommandValidator : AbstractValidator<CreateMenuCategoryCommand>
{
    public CreateMenuCategoryCommandValidator()
    {
        RuleFor(c => c.RestaurantId).NotEqual(Guid.Empty);
        RuleFor(c => c.Name).NotEmpty().MaximumLength(200);
        RuleFor(c => c.DisplayOrder).GreaterThanOrEqualTo(0);
    }
}

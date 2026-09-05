using FluentValidation;
using FoodDeliveryService.Modules.Restaurants.Application.Restaurants.CreateMenuItem;

namespace FoodDeliveryService.Modules.Restaurants.Application.Restaurants.UpdateMenuItem;

internal sealed class UpdateMenuItemCommandValidator : AbstractValidator<UpdateMenuItemCommand>
{
    public UpdateMenuItemCommandValidator()
    {
        RuleFor(c => c.RestaurantId).NotEqual(Guid.Empty);
        RuleFor(c => c.MenuItemId).NotEqual(Guid.Empty);
        RuleFor(c => c.Name).NotEmpty().MaximumLength(200);
        RuleFor(c => c.Description).NotNull().MaximumLength(1000);
        // Bounded by the numeric(10,2) column, exactly as on creation — see
        // CreateMenuItemCommandValidator.
        RuleFor(c => c.Price).GreaterThan(0).LessThanOrEqualTo(CreateMenuItemCommandValidator.MaxPrice);
        RuleFor(c => c.PhotoUrl).MaximumLength(1000);
    }
}

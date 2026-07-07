using FluentValidation;

namespace FoodDeliveryService.Modules.Restaurants.Application.Restaurants.UpdateMenuItem;

internal sealed class UpdateMenuItemCommandValidator : AbstractValidator<UpdateMenuItemCommand>
{
    public UpdateMenuItemCommandValidator()
    {
        RuleFor(c => c.RestaurantId).NotEqual(Guid.Empty);
        RuleFor(c => c.MenuItemId).NotEqual(Guid.Empty);
        RuleFor(c => c.Name).NotEmpty().MaximumLength(200);
        RuleFor(c => c.Description).NotNull().MaximumLength(1000);
        RuleFor(c => c.Price).GreaterThan(0);
        RuleFor(c => c.PhotoUrl).MaximumLength(1000);
    }
}

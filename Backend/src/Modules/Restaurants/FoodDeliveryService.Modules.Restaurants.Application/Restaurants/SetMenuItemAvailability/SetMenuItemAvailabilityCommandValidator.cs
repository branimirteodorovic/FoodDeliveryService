using FluentValidation;

namespace FoodDeliveryService.Modules.Restaurants.Application.Restaurants.SetMenuItemAvailability;

internal sealed class SetMenuItemAvailabilityCommandValidator : AbstractValidator<SetMenuItemAvailabilityCommand>
{
    public SetMenuItemAvailabilityCommandValidator()
    {
        RuleFor(c => c.RestaurantId).NotEqual(Guid.Empty);
        RuleFor(c => c.MenuItemId).NotEqual(Guid.Empty);
    }
}

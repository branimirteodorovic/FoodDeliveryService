using FluentValidation;

namespace FoodDeliveryService.Modules.Restaurants.Application.Restaurants.OnboardRestaurant;

internal sealed class OnboardRestaurantCommandValidator : AbstractValidator<OnboardRestaurantCommand>
{
    public OnboardRestaurantCommandValidator()
    {
        RuleFor(c => c.Name).NotEmpty().MaximumLength(300);
        RuleFor(c => c.TaxIdentification).NotEmpty().MaximumLength(100);
        RuleFor(c => c.CuisineType).NotEmpty().MaximumLength(100);
        RuleFor(c => c.Email).NotEmpty().EmailAddress().MaximumLength(300);
        RuleFor(c => c.PhoneNumber).NotEmpty().MaximumLength(50);

        RuleFor(c => c.Street).NotEmpty().MaximumLength(300);
        RuleFor(c => c.City).NotEmpty().MaximumLength(200);
        RuleFor(c => c.PostalCode).NotEmpty().MaximumLength(20);
        RuleFor(c => c.Country).NotEmpty().MaximumLength(100);
        // Required — a restaurant without coordinates can never be assigned a driver (Feature 2.1).
        RuleFor(c => c.Latitude).NotNull().InclusiveBetween(-90, 90);
        RuleFor(c => c.Longitude).NotNull().InclusiveBetween(-180, 180);

        // Fraction, not a percentage: 0.20 = 20%. Range mirrors the domain guard
        // (RestaurantErrors.InvalidCommissionRate).
        RuleFor(c => c.CommissionRate).GreaterThanOrEqualTo(0).LessThan(1);

        RuleFor(c => c.ManagerEmail).NotEmpty().EmailAddress().MaximumLength(300);
        RuleFor(c => c.ManagerFirstName).NotEmpty().MaximumLength(200);
        RuleFor(c => c.ManagerLastName).NotEmpty().MaximumLength(200);
    }
}

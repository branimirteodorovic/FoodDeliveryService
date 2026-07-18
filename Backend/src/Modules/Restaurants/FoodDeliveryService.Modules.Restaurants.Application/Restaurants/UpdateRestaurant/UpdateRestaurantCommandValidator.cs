using FluentValidation;

namespace FoodDeliveryService.Modules.Restaurants.Application.Restaurants.UpdateRestaurant;

internal sealed class UpdateRestaurantCommandValidator : AbstractValidator<UpdateRestaurantCommand>
{
    public UpdateRestaurantCommandValidator()
    {
        RuleFor(c => c.RestaurantId).NotEqual(Guid.Empty);
        RuleFor(c => c.Name).NotEmpty().MaximumLength(300);
        RuleFor(c => c.TaxIdentification).NotEmpty().MaximumLength(100);
        RuleFor(c => c.CuisineType).NotEmpty().MaximumLength(100);
        RuleFor(c => c.Email).NotEmpty().EmailAddress().MaximumLength(300);
        RuleFor(c => c.PhoneNumber).NotEmpty().MaximumLength(50);
        RuleFor(c => c.Street).NotEmpty().MaximumLength(300);
        RuleFor(c => c.City).NotEmpty().MaximumLength(200);
        RuleFor(c => c.PostalCode).NotEmpty().MaximumLength(20);
        RuleFor(c => c.Country).NotEmpty().MaximumLength(100);
        // Required — coordinates back the delivery-assignment geo search (Feature 2.1).
        RuleFor(c => c.Latitude).NotNull().InclusiveBetween(-90, 90);
        RuleFor(c => c.Longitude).NotNull().InclusiveBetween(-180, 180);
    }
}

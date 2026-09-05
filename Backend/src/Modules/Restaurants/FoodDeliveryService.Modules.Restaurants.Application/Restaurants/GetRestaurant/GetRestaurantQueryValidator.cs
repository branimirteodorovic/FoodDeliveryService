using FluentValidation;

namespace FoodDeliveryService.Modules.Restaurants.Application.Restaurants.GetRestaurant;

internal sealed class GetRestaurantQueryValidator : AbstractValidator<GetRestaurantQuery>
{
    public GetRestaurantQueryValidator()
    {
        RuleFor(q => q.RestaurantId).NotEmpty();
    }
}

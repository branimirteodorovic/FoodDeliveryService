using FluentValidation;

namespace FoodDeliveryService.Modules.Restaurants.Application.Restaurants.GetRestaurants;

internal sealed class GetRestaurantsQueryValidator : AbstractValidator<GetRestaurantsQuery>
{
    public GetRestaurantsQueryValidator()
    {
        RuleFor(q => q.Page).GreaterThanOrEqualTo(1);
        RuleFor(q => q.PageSize).InclusiveBetween(1, 100);
    }
}

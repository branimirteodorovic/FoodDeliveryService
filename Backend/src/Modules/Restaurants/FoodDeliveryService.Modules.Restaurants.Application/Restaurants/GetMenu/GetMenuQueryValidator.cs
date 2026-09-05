using FluentValidation;

namespace FoodDeliveryService.Modules.Restaurants.Application.Restaurants.GetMenu;

/// <summary>
/// Validation runs <em>before</em> <c>QueryCachingBehavior</c> in the pipeline, which is what makes
/// this worth having on a cached query: a rejected id never reaches the cache, so an empty-Guid
/// probe cannot write a key that a later legitimate call reads back.
/// </summary>
internal sealed class GetMenuQueryValidator : AbstractValidator<GetMenuQuery>
{
    public GetMenuQueryValidator()
    {
        RuleFor(q => q.RestaurantId).NotEmpty();
    }
}

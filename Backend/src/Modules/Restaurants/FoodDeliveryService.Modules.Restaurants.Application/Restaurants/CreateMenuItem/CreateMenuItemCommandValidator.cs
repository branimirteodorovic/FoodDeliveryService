using FluentValidation;

namespace FoodDeliveryService.Modules.Restaurants.Application.Restaurants.CreateMenuItem;

internal sealed class CreateMenuItemCommandValidator : AbstractValidator<CreateMenuItemCommand>
{
    public CreateMenuItemCommandValidator()
    {
        RuleFor(c => c.RestaurantId).NotEqual(Guid.Empty);
        RuleFor(c => c.CategoryId).NotEqual(Guid.Empty);
        RuleFor(c => c.Name).NotEmpty().MaximumLength(200);
        RuleFor(c => c.Description).NotNull().MaximumLength(1000);
        // Feature 3.7 Milestone F. Upper-bounded as well as positive: the column is
        // numeric(10,2), so a price beyond it is an Npgsql overflow — a 500 carrying a database
        // message — where the caller should have been told 400.
        RuleFor(c => c.Price).GreaterThan(0).LessThanOrEqualTo(MaxPrice);
        RuleFor(c => c.PhotoUrl).MaximumLength(1000);
    }

    /// <summary>
    /// The largest price the <c>numeric(10,2)</c> column can hold with room to spare. Shared with
    /// <c>UpdateMenuItemCommandValidator</c> so the two cannot drift apart.
    /// </summary>
    internal const decimal MaxPrice = 100_000m;
}

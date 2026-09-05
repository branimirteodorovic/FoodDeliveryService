using FluentValidation;
using FoodDeliveryService.Modules.Orders.Domain.Orders;

namespace FoodDeliveryService.Modules.Orders.Application.Orders.PlaceOrder;

internal sealed class PlaceOrderCommandValidator : AbstractValidator<PlaceOrderCommand>
{
    public PlaceOrderCommandValidator()
    {
        RuleFor(c => c.RestaurantId).NotEqual(Guid.Empty);

        RuleFor(c => c.Items).NotEmpty();

        RuleForEach(c => c.Items).ChildRules(item =>
        {
            item.RuleFor(i => i.MenuItemId).NotEqual(Guid.Empty);
            // Feature 3.7 Milestone F. Upper-bounded as well as positive. Quantity multiplies the
            // unit price into a numeric(10,2) line total, so an unbounded one is an arithmetic
            // overflow inside the aggregate rather than a rejected request.
            item.RuleFor(i => i.Quantity).InclusiveBetween(1, MaxQuantityPerItem);
        });

        RuleFor(c => c.Street).NotEmpty().MaximumLength(300);
        RuleFor(c => c.City).NotEmpty().MaximumLength(200);
        RuleFor(c => c.PostalCode).NotEmpty().MaximumLength(20);
        RuleFor(c => c.Country).NotEmpty().MaximumLength(100);
        RuleFor(c => c.Notes).MaximumLength(500);

        // Required — the client app supplies the map pin; the Delivery service routes to it.
        RuleFor(c => c.Latitude).NotNull().InclusiveBetween(-90, 90);
        RuleFor(c => c.Longitude).NotNull().InclusiveBetween(-180, 180);

        RuleFor(c => c.PaymentMethod)
            .Must(method => Enum.TryParse<PaymentMethod>(method, ignoreCase: true, out _))
            .WithMessage("The specified payment method is not supported.");

        // Business checks (unknown/unavailable item, unknown restaurant) live in the handler and
        // the domain — the validator only guards input shape.
        RuleFor(c => c.IdempotencyKey).NotEmpty().MaximumLength(100);
    }

    /// <summary>
    /// A per-line ceiling, not a per-order one: a hundred of one dish is already an unusual order,
    /// and the point is to keep the line total inside <c>numeric(10,2)</c>.
    /// </summary>
    private const int MaxQuantityPerItem = 100;
}

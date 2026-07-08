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
            item.RuleFor(i => i.Quantity).GreaterThan(0);
        });

        RuleFor(c => c.Street).NotEmpty().MaximumLength(300);
        RuleFor(c => c.City).NotEmpty().MaximumLength(200);
        RuleFor(c => c.PostalCode).NotEmpty().MaximumLength(20);
        RuleFor(c => c.Country).NotEmpty().MaximumLength(100);
        RuleFor(c => c.Notes).MaximumLength(500);

        RuleFor(c => c.PaymentMethod)
            .Must(method => Enum.TryParse<PaymentMethod>(method, ignoreCase: true, out _))
            .WithMessage("The specified payment method is not supported.");

        // Business checks (unknown/unavailable item, unknown restaurant) live in the handler and
        // the domain — the validator only guards input shape.
        RuleFor(c => c.IdempotencyKey).NotEmpty().MaximumLength(100);
    }
}

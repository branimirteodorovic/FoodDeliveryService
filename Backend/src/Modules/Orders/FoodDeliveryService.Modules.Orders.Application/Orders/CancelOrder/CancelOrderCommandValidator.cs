using FluentValidation;

namespace FoodDeliveryService.Modules.Orders.Application.Orders.CancelOrder;

internal sealed class CancelOrderCommandValidator : AbstractValidator<CancelOrderCommand>
{
    public CancelOrderCommandValidator()
    {
        RuleFor(c => c.OrderId).NotEmpty();
    }
}

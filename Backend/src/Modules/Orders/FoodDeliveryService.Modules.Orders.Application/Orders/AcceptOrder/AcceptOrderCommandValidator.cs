using FluentValidation;

namespace FoodDeliveryService.Modules.Orders.Application.Orders.AcceptOrder;

internal sealed class AcceptOrderCommandValidator : AbstractValidator<AcceptOrderCommand>
{
    public AcceptOrderCommandValidator()
    {
        RuleFor(c => c.OrderId).NotEmpty();
    }
}

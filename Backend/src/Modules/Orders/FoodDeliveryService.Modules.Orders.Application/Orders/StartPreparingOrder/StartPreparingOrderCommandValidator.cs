using FluentValidation;

namespace FoodDeliveryService.Modules.Orders.Application.Orders.StartPreparingOrder;

internal sealed class StartPreparingOrderCommandValidator : AbstractValidator<StartPreparingOrderCommand>
{
    public StartPreparingOrderCommandValidator()
    {
        RuleFor(c => c.OrderId).NotEmpty();
    }
}

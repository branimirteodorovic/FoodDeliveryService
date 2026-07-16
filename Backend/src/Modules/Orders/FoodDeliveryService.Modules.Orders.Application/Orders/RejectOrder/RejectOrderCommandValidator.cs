using FluentValidation;

namespace FoodDeliveryService.Modules.Orders.Application.Orders.RejectOrder;

internal sealed class RejectOrderCommandValidator : AbstractValidator<RejectOrderCommand>
{
    public RejectOrderCommandValidator()
    {
        RuleFor(c => c.OrderId).NotEqual(Guid.Empty);

        RuleFor(c => c.Reason).NotEmpty().MaximumLength(500);
    }
}

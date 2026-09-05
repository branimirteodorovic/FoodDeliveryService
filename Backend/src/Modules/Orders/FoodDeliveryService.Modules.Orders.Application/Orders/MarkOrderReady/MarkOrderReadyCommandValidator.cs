using FluentValidation;

namespace FoodDeliveryService.Modules.Orders.Application.Orders.MarkOrderReady;

internal sealed class MarkOrderReadyCommandValidator : AbstractValidator<MarkOrderReadyCommand>
{
    public MarkOrderReadyCommandValidator()
    {
        RuleFor(c => c.OrderId).NotEmpty();
    }
}

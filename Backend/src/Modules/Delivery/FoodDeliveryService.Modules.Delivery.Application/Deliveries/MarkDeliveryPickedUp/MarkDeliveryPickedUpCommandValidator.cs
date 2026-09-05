using FluentValidation;

namespace FoodDeliveryService.Modules.Delivery.Application.Deliveries.MarkDeliveryPickedUp;

internal sealed class MarkDeliveryPickedUpCommandValidator : AbstractValidator<MarkDeliveryPickedUpCommand>
{
    public MarkDeliveryPickedUpCommandValidator()
    {
        RuleFor(c => c.DeliveryId).NotEmpty();
    }
}

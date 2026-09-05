using FluentValidation;

namespace FoodDeliveryService.Modules.Delivery.Application.Deliveries.MarkDeliveryDelivered;

internal sealed class MarkDeliveryDeliveredCommandValidator : AbstractValidator<MarkDeliveryDeliveredCommand>
{
    public MarkDeliveryDeliveredCommandValidator()
    {
        RuleFor(c => c.DeliveryId).NotEmpty();
    }
}

using FluentValidation;

namespace FoodDeliveryService.Modules.Delivery.Application.Deliveries.GetDelivery;

internal sealed class GetDeliveryQueryValidator : AbstractValidator<GetDeliveryQuery>
{
    public GetDeliveryQueryValidator()
    {
        RuleFor(q => q.DeliveryId).NotEmpty();
    }
}

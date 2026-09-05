using FluentValidation;

namespace FoodDeliveryService.Modules.Delivery.Application.Deliveries.GetDeliveryByOrder;

internal sealed class GetDeliveryByOrderQueryValidator : AbstractValidator<GetDeliveryByOrderQuery>
{
    public GetDeliveryByOrderQueryValidator()
    {
        RuleFor(q => q.OrderId).NotEmpty();
    }
}

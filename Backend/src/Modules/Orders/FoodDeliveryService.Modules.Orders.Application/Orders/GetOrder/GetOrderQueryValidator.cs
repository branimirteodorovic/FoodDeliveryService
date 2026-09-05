using FluentValidation;

namespace FoodDeliveryService.Modules.Orders.Application.Orders.GetOrder;

internal sealed class GetOrderQueryValidator : AbstractValidator<GetOrderQuery>
{
    public GetOrderQueryValidator()
    {
        RuleFor(q => q.OrderId).NotEmpty();
    }
}

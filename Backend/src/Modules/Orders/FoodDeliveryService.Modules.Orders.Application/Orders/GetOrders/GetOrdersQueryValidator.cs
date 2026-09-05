using FluentValidation;

namespace FoodDeliveryService.Modules.Orders.Application.Orders.GetOrders;

/// <summary>
/// Feature 3.7 Milestone F. The page size is bounded, not merely positive — see
/// <c>GetDeliveriesQueryValidator</c> for the reasoning; this query had the same hole.
/// </summary>
internal sealed class GetOrdersQueryValidator : AbstractValidator<GetOrdersQuery>
{
    public GetOrdersQueryValidator()
    {
        RuleFor(q => q.Page).GreaterThanOrEqualTo(1);
        RuleFor(q => q.PageSize).InclusiveBetween(1, 100);
    }
}

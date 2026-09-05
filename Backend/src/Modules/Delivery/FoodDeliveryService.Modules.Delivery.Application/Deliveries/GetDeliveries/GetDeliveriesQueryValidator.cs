using FluentValidation;

namespace FoodDeliveryService.Modules.Delivery.Application.Deliveries.GetDeliveries;

/// <summary>
/// Feature 3.7 Milestone F. The page size is bounded, not merely positive: the endpoint defaults it
/// to 20 but a caller sending <c>?pageSize=1000000</c> was previously served a million-row scan that
/// the edge rate limiter counts as a single request.
/// </summary>
internal sealed class GetDeliveriesQueryValidator : AbstractValidator<GetDeliveriesQuery>
{
    public GetDeliveriesQueryValidator()
    {
        RuleFor(q => q.Page).GreaterThanOrEqualTo(1);
        RuleFor(q => q.PageSize).InclusiveBetween(1, 100);
    }
}

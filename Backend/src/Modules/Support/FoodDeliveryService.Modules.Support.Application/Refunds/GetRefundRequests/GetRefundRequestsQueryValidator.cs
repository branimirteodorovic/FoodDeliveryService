using FluentValidation;
using FoodDeliveryService.Modules.Support.Domain.Refunds;

namespace FoodDeliveryService.Modules.Support.Application.Refunds.GetRefundRequests;

internal sealed class GetRefundRequestsQueryValidator : AbstractValidator<GetRefundRequestsQuery>
{
    public GetRefundRequestsQueryValidator()
    {
        RuleFor(q => q.Page).GreaterThan(0);

        // An unbounded page size is a denial-of-service parameter on a table that only grows.
        RuleFor(q => q.PageSize).InclusiveBetween(1, 100);

        RuleFor(q => q.Status)
            .Must(status => status is null || Enum.TryParse<RefundStatus>(status, ignoreCase: true, out _))
            .WithMessage($"Status must be one of: {string.Join(", ", Enum.GetNames<RefundStatus>())}");
    }
}

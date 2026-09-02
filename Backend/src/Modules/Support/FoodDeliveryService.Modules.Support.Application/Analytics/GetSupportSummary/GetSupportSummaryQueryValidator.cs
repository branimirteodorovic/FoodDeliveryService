using FluentValidation;

namespace FoodDeliveryService.Modules.Support.Application.Analytics.GetSupportSummary;

internal sealed class GetSupportSummaryQueryValidator : AbstractValidator<GetSupportSummaryQuery>
{
    /// <summary>
    /// A little over a year, so a full-year comparison fits and an accidental <c>from=0001-01-01</c>
    /// does not. The bound is on the gap-filled day series more than on the aggregates: one row per
    /// day is cheap, one row per day since the Common Era is a response nobody asked for.
    /// </summary>
    private const int MaxWindowInDays = 366;

    public GetSupportSummaryQueryValidator()
    {
        RuleFor(q => q.ToUtc)
            .GreaterThan(q => q.FromUtc)
            .WithMessage("'to' must be later than 'from'.");

        RuleFor(q => q)
            .Must(q => (q.ToUtc - q.FromUtc).TotalDays <= MaxWindowInDays)
            .WithMessage($"The reporting window must be at most {MaxWindowInDays} days.");
    }
}

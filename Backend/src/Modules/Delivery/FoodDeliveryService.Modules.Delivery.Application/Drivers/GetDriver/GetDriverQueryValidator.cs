using FluentValidation;

namespace FoodDeliveryService.Modules.Delivery.Application.Drivers.GetDriver;

/// <summary>
/// A null <c>DriverId</c> is the documented "my own profile" case and must stay valid; an id that is
/// present but empty is a caller sending <c>Guid.Empty</c>, which is not the same request and must
/// not be answered as if it were.
/// </summary>
internal sealed class GetDriverQueryValidator : AbstractValidator<GetDriverQuery>
{
    public GetDriverQueryValidator()
    {
        RuleFor(q => q.DriverId)
            .NotEqual(Guid.Empty)
            .When(q => q.DriverId.HasValue);
    }
}

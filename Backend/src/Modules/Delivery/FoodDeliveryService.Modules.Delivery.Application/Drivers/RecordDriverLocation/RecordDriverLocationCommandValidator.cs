using FluentValidation;

namespace FoodDeliveryService.Modules.Delivery.Application.Drivers.RecordDriverLocation;

internal sealed class RecordDriverLocationCommandValidator : AbstractValidator<RecordDriverLocationCommand>
{
    public RecordDriverLocationCommandValidator()
    {
        // Range is re-asserted by GeoCoordinate.Create in the domain; validating here turns a bad
        // payload into a clean 400 instead of a business failure.
        RuleFor(c => c.Latitude).InclusiveBetween(-90, 90);
        RuleFor(c => c.Longitude).InclusiveBetween(-180, 180);
    }
}

using FluentValidation;
using FoodDeliveryService.Modules.Delivery.Domain.Drivers;

namespace FoodDeliveryService.Modules.Delivery.Application.Drivers.UpdateDriverProfile;

internal sealed class UpdateDriverProfileCommandValidator : AbstractValidator<UpdateDriverProfileCommand>
{
    public UpdateDriverProfileCommandValidator()
    {
        RuleFor(c => c.FirstName).NotEmpty().MaximumLength(200);
        RuleFor(c => c.LastName).NotEmpty().MaximumLength(200);
        RuleFor(c => c.VehicleType)
            .NotEmpty()
            .Must(v => Enum.TryParse<VehicleType>(v, ignoreCase: true, out _))
            .WithMessage("VehicleType must be one of: Bicycle, Motorcycle, Car.");
    }
}

using FluentValidation;

namespace FoodDeliveryService.Modules.Delivery.Application.Deliveries.CreateDelivery;

internal sealed class CreateDeliveryCommandValidator : AbstractValidator<CreateDeliveryCommand>
{
    public CreateDeliveryCommandValidator()
    {
        RuleFor(c => c.OrderId).NotEmpty();
        RuleFor(c => c.RestaurantId).NotEmpty();
        RuleFor(c => c.CustomerId).NotEmpty();
        RuleFor(c => c.PickupLatitude).InclusiveBetween(-90, 90);
        RuleFor(c => c.PickupLongitude).InclusiveBetween(-180, 180);
        RuleFor(c => c.DropoffStreet).NotEmpty().MaximumLength(300);
        RuleFor(c => c.DropoffCity).NotEmpty().MaximumLength(200);
        RuleFor(c => c.DropoffPostalCode).NotEmpty().MaximumLength(20);
        RuleFor(c => c.DropoffCountry).NotEmpty().MaximumLength(100);
        RuleFor(c => c.DropoffNotes).MaximumLength(500);
        RuleFor(c => c.DropoffLatitude).InclusiveBetween(-90, 90);
        RuleFor(c => c.DropoffLongitude).InclusiveBetween(-180, 180);
    }
}

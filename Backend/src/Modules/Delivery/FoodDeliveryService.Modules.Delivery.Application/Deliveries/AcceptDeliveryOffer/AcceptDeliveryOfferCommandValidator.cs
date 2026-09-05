using FluentValidation;

namespace FoodDeliveryService.Modules.Delivery.Application.Deliveries.AcceptDeliveryOffer;

internal sealed class AcceptDeliveryOfferCommandValidator : AbstractValidator<AcceptDeliveryOfferCommand>
{
    public AcceptDeliveryOfferCommandValidator()
    {
        RuleFor(c => c.DeliveryId).NotEmpty();
    }
}

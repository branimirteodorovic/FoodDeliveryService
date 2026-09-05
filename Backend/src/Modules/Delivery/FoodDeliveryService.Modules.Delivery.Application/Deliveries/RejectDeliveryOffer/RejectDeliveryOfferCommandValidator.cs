using FluentValidation;

namespace FoodDeliveryService.Modules.Delivery.Application.Deliveries.RejectDeliveryOffer;

internal sealed class RejectDeliveryOfferCommandValidator : AbstractValidator<RejectDeliveryOfferCommand>
{
    public RejectDeliveryOfferCommandValidator()
    {
        RuleFor(c => c.DeliveryId).NotEmpty();
    }
}

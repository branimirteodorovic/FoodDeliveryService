using FluentValidation;

namespace FoodDeliveryService.Modules.Payments.Application.PaymentMethods.DetachPaymentMethod;

internal sealed class DetachPaymentMethodCommandValidator : AbstractValidator<DetachPaymentMethodCommand>
{
    public DetachPaymentMethodCommandValidator()
    {
        RuleFor(c => c.PaymentMethodId).NotEmpty();
    }
}

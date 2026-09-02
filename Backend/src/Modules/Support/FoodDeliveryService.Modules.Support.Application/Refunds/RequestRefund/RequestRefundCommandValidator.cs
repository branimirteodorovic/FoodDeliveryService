using FluentValidation;
using FoodDeliveryService.Modules.Support.Domain.Refunds;

namespace FoodDeliveryService.Modules.Support.Application.Refunds.RequestRefund;

internal sealed class RequestRefundCommandValidator : AbstractValidator<RequestRefundCommand>
{
    public RequestRefundCommandValidator()
    {
        // The aggregate enforces the positive-amount and reason rules too, and that copy is the one
        // that matters. This one only turns a malformed request into a 400 before three database
        // round trips. The subtotal ceiling is deliberately NOT duplicated here: it depends on data
        // this layer has not read yet, and a validator that guesses at it would drift.
        RuleFor(c => c.Amount).GreaterThan(0);

        RuleFor(c => c.Reason).NotEmpty().MaximumLength(RefundRequest.ReasonMaxLength);
    }
}

using FluentValidation;

namespace FoodDeliveryService.Modules.Payments.Application.PaymentMethods.AttachPaymentMethod;

internal sealed class AttachPaymentMethodCommandValidator : AbstractValidator<AttachPaymentMethodCommand>
{
    /// <summary>
    /// Stripe's identifiers are well under this; the bound exists so an unbounded string cannot
    /// reach the provider, the row and every log line that quotes it.
    /// </summary>
    private const int MaximumPaymentMethodIdLength = 255;

    public AttachPaymentMethodCommandValidator()
    {
        RuleFor(c => c.CustomerId).NotEmpty();

        // The prefix check is a guard against the wrong kind of Stripe identifier, not an attempt to
        // validate the id — only Stripe can do that, and it does, on the attach call. Passing a
        // `seti_…` or a `pi_…` here otherwise surfaces as an opaque provider error.
        RuleFor(c => c.StripePaymentMethodId)
            .NotEmpty()
            .MaximumLength(MaximumPaymentMethodIdLength)
            .Must(id => id.StartsWith("pm_", StringComparison.Ordinal))
            .WithMessage("A Stripe payment method identifier starts with pm_.");
    }
}

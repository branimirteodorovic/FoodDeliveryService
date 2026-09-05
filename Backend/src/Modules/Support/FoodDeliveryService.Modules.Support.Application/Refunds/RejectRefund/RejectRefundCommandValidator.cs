using FluentValidation;
using FoodDeliveryService.Modules.Support.Domain.Refunds;

namespace FoodDeliveryService.Modules.Support.Application.Refunds.RejectRefund;

internal sealed class RejectRefundCommandValidator : AbstractValidator<RejectRefundCommand>
{
    public RejectRefundCommandValidator()
    {
        RuleFor(c => c.RefundRequestId).NotEmpty();

        RuleFor(c => c.Note).MaximumLength(RefundRequest.DecisionNoteMaxLength);
    }
}

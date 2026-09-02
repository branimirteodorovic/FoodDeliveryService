using FluentValidation;
using FoodDeliveryService.Modules.Support.Domain.Refunds;

namespace FoodDeliveryService.Modules.Support.Application.Refunds.ApproveRefund;

internal sealed class ApproveRefundCommandValidator : AbstractValidator<ApproveRefundCommand>
{
    public ApproveRefundCommandValidator()
    {
        // Optional. The note is context for the customer's email and for the audit entry, not a
        // condition of the decision — and an over-long one is truncated on the entry rather than
        // costing an administrator the action they came to perform.
        RuleFor(c => c.Note).MaximumLength(RefundRequest.DecisionNoteMaxLength);
    }
}

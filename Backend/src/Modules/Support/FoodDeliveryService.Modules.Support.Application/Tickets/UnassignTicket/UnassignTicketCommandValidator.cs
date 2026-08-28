using FluentValidation;
using FoodDeliveryService.Modules.Support.Domain.Audit;

namespace FoodDeliveryService.Modules.Support.Application.Tickets.UnassignTicket;

internal sealed class UnassignTicketCommandValidator : AbstractValidator<UnassignTicketCommand>
{
    public UnassignTicketCommandValidator()
    {
        RuleFor(c => c.TicketId).NotEmpty();
        RuleFor(c => c.Reason).NotEmpty().MaximumLength(SupportAuditEntry.ReasonMaxLength);
    }
}

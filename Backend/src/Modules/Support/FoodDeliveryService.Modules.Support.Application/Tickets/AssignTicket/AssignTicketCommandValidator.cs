using FluentValidation;
using FoodDeliveryService.Modules.Support.Domain.Audit;

namespace FoodDeliveryService.Modules.Support.Application.Tickets.AssignTicket;

internal sealed class AssignTicketCommandValidator : AbstractValidator<AssignTicketCommand>
{
    public AssignTicketCommandValidator()
    {
        RuleFor(c => c.TicketId).NotEmpty();
        RuleFor(c => c.AgentId).NotEmpty();

        // Bounded to what the audit entry stores, so a reason is never silently truncated to
        // something shorter than what the agent was told was accepted.
        RuleFor(c => c.Reason).MaximumLength(SupportAuditEntry.ReasonMaxLength);
    }
}

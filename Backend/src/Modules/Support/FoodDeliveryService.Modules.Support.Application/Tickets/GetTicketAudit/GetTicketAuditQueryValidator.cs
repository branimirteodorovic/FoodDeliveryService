using FluentValidation;

namespace FoodDeliveryService.Modules.Support.Application.Tickets.GetTicketAudit;

internal sealed class GetTicketAuditQueryValidator : AbstractValidator<GetTicketAuditQuery>
{
    public GetTicketAuditQueryValidator()
    {
        RuleFor(q => q.TicketId).NotEmpty();
    }
}

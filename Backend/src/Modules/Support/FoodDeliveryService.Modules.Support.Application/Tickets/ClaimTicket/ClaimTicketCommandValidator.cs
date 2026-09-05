using FluentValidation;

namespace FoodDeliveryService.Modules.Support.Application.Tickets.ClaimTicket;

internal sealed class ClaimTicketCommandValidator : AbstractValidator<ClaimTicketCommand>
{
    public ClaimTicketCommandValidator()
    {
        RuleFor(c => c.TicketId).NotEmpty();
    }
}

using FluentValidation;

namespace FoodDeliveryService.Modules.Support.Application.Tickets.GetTicket;

internal sealed class GetTicketQueryValidator : AbstractValidator<GetTicketQuery>
{
    public GetTicketQueryValidator()
    {
        RuleFor(q => q.TicketId).NotEmpty();
    }
}

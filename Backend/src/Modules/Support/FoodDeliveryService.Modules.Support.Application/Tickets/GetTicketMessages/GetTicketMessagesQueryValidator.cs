using FluentValidation;

namespace FoodDeliveryService.Modules.Support.Application.Tickets.GetTicketMessages;

internal sealed class GetTicketMessagesQueryValidator : AbstractValidator<GetTicketMessagesQuery>
{
    public GetTicketMessagesQueryValidator()
    {
        RuleFor(q => q.TicketId).NotEmpty();
    }
}

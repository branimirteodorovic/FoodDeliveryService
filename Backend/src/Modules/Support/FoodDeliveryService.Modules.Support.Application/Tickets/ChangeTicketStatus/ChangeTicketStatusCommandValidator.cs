using FluentValidation;
using FoodDeliveryService.Modules.Support.Domain.Tickets;

namespace FoodDeliveryService.Modules.Support.Application.Tickets.ChangeTicketStatus;

internal sealed class ChangeTicketStatusCommandValidator : AbstractValidator<ChangeTicketStatusCommand>
{
    public ChangeTicketStatusCommandValidator()
    {
        RuleFor(c => c.TicketId).NotEmpty();

        // Only that the name parses. Whether the move is legal from the ticket's current state is
        // the aggregate's call, and it is the only place that knows the current state.
        RuleFor(c => c.Status)
            .Must(status => Enum.TryParse<TicketStatus>(status, ignoreCase: true, out _))
            .WithMessage($"Status must be one of: {string.Join(", ", Enum.GetNames<TicketStatus>())}");

        RuleFor(c => c.Reason).MaximumLength(2000);
    }
}

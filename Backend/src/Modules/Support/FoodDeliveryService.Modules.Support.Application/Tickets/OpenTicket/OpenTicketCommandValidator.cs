using FluentValidation;
using FoodDeliveryService.Modules.Support.Domain.Tickets;

namespace FoodDeliveryService.Modules.Support.Application.Tickets.OpenTicket;

internal sealed class OpenTicketCommandValidator : AbstractValidator<OpenTicketCommand>
{
    public OpenTicketCommandValidator()
    {
        // The aggregate enforces this too, and that is the copy that matters — this one only turns
        // a malformed request into a 400 before a database round-trip.
        RuleFor(c => c.Subject).NotEmpty().MaximumLength(Ticket.SubjectMaxLength);

        RuleFor(c => c.Category)
            .Must(category => Enum.TryParse<TicketCategory>(category, ignoreCase: true, out _))
            .WithMessage($"Category must be one of: {string.Join(", ", Enum.GetNames<TicketCategory>())}");
    }
}

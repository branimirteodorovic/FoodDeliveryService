using FluentValidation;
using FoodDeliveryService.Modules.Support.Domain.Tickets;

namespace FoodDeliveryService.Modules.Support.Application.Tickets.PostTicketMessage;

internal sealed class PostTicketMessageCommandValidator : AbstractValidator<PostTicketMessageCommand>
{
    public PostTicketMessageCommandValidator()
    {
        // The aggregate enforces both of these too, and that is the copy that matters — this one
        // only turns a malformed request into a 400 before a database round-trip.
        RuleFor(c => c.Body).NotEmpty().MaximumLength(TicketMessage.BodyMaxLength);

        RuleFor(c => c.Visibility)
            .Must(visibility => Enum.TryParse<TicketMessageVisibility>(visibility, ignoreCase: true, out _))
            .WithMessage(
                $"Visibility must be one of: {string.Join(", ", Enum.GetNames<TicketMessageVisibility>())}");
    }
}

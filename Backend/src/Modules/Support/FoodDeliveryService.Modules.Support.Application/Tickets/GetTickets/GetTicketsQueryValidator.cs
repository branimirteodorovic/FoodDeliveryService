using FluentValidation;
using FoodDeliveryService.Modules.Support.Domain.Tickets;

namespace FoodDeliveryService.Modules.Support.Application.Tickets.GetTickets;

internal sealed class GetTicketsQueryValidator : AbstractValidator<GetTicketsQuery>
{
    public GetTicketsQueryValidator()
    {
        RuleFor(q => q.Page).GreaterThan(0);

        // An unbounded page size is a denial-of-service parameter on a table that only grows.
        RuleFor(q => q.PageSize).InclusiveBetween(1, 100);

        RuleFor(q => q.Status)
            .Must(status => status is null || Enum.TryParse<TicketStatus>(status, ignoreCase: true, out _))
            .WithMessage($"Status must be one of: {string.Join(", ", Enum.GetNames<TicketStatus>())}");

        RuleFor(q => q.Category)
            .Must(category => category is null || Enum.TryParse<TicketCategory>(category, ignoreCase: true, out _))
            .WithMessage($"Category must be one of: {string.Join(", ", Enum.GetNames<TicketCategory>())}");

        RuleFor(q => q.To)
            .GreaterThanOrEqualTo(q => q!.From!.Value)
            .When(q => q.From is not null && q.To is not null);
    }
}

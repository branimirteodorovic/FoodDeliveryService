using FoodDeliveryService.Common.Domain;

namespace FoodDeliveryService.Modules.Support.Application.Tickets;

/// <summary>
/// Authorization failures that belong to the application layer rather than to the aggregate — the
/// aggregate has no notion of who is calling, only of what state it is in. Everything about the
/// ticket itself lives in <c>TicketErrors</c>.
/// </summary>
internal static class SupportErrors
{
    internal static readonly Error NotAuthorizedToActOnBehalfOfCustomer = Error.Problem(
        "Support.NotAuthorizedToActOnBehalfOfCustomer",
        "Only a support agent may open a ticket on behalf of another customer");
}

using FoodDeliveryService.Modules.Support.Application.Abstractions.Authentication;

namespace FoodDeliveryService.Modules.Support.Application.Tickets;

/// <summary>
/// The one place that answers "is this caller staff?". Staff means holding
/// <c>support-tickets:manage</c>, which agents and administrators have and customers never do —
/// the same ownership-bypass shape Orders and Restaurants use.
///
/// Every read in this module is scoped through it, and a customer who fails the check does not get
/// a 403 on somebody else's ticket: they get a 404. A 403 confirms the ticket exists, which is
/// exactly what a customer probing for other customers' ticket ids is trying to learn.
/// </summary>
internal static class TicketAccess
{
    internal static bool IsStaff(ISupportContext context) => context.HasPermission(Permissions.ManageTickets);
}

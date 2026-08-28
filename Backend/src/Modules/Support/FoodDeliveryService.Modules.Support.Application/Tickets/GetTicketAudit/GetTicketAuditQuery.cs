using FoodDeliveryService.Common.Application.Messaging;

namespace FoodDeliveryService.Modules.Support.Application.Tickets.GetTicketAudit;

// Staff-only, and unlike every other ticket read it is not ownership-scoped to a customer at all:
// the entries carry internal reasons, so there is no version of this list a customer may see.
public sealed record GetTicketAuditQuery(Guid TicketId) : IQuery<IReadOnlyCollection<SupportAuditEntryResponse>>;

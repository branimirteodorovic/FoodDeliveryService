using System.Globalization;
using FoodDeliveryService.Modules.Support.Domain.Tickets;
using FoodDeliveryService.Modules.Support.Infrastructure.Database;
using Microsoft.EntityFrameworkCore;

namespace FoodDeliveryService.Modules.Support.Infrastructure.Tickets;

internal sealed class TicketsRepository(SupportDbContext context) : ITicketsRepository
{
    private const string ReferencePrefix = "SUP-";

    public async Task<Ticket?> GetAsync(Guid ticketId, CancellationToken cancellationToken = default)
    {
        return await context.Tickets.SingleOrDefaultAsync(t => t.Id == ticketId, cancellationToken);
    }

    public async Task<string> NextReferenceAsync(CancellationToken cancellationToken = default)
    {
        // nextval() is the whole point: it is atomic, it is not transactional (so two concurrent
        // opens never collide and never wait on each other), and it does not read the table — a
        // MAX(reference)+1 would do all three things wrong the moment this service runs two pods.
        // Raw, not interpolated: EF turns an interpolation hole into a bind parameter, and a bind
        // parameter inside the quoted literal nextval() expects would be sent as the literal text
        // rather than as the sequence name. There is no user input in this string.
        long next = await context.Database
            .SqlQueryRaw<long>($"SELECT nextval('{SupportDbContext.TicketReferenceSequence}') AS \"Value\"")
            .SingleAsync(cancellationToken);

        return ReferencePrefix + next.ToString("D8", CultureInfo.InvariantCulture);
    }

    public void Insert(Ticket ticket)
    {
        context.Tickets.Add(ticket);
    }
}

namespace FoodDeliveryService.Modules.Support.Domain.Tickets;

public interface ITicketsRepository
{
    Task<Ticket?> GetAsync(Guid ticketId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Allocates the next human-quotable reference (SUP-00001234) from the Postgres sequence.
    /// A sequence rather than MAX()+1 because two replicas reading the current maximum at the same
    /// instant would both write the same reference — and a sequence hands out its number outside
    /// the transaction, so a rolled-back ticket burns a number instead of blocking the next one.
    /// </summary>
    Task<string> NextReferenceAsync(CancellationToken cancellationToken = default);

    void Insert(Ticket ticket);
}

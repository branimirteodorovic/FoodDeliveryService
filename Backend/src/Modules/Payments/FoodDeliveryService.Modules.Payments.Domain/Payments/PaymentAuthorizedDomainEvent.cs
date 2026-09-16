using FoodDeliveryService.Common.Domain;

namespace FoodDeliveryService.Modules.Payments.Domain.Payments;

/// <summary>
/// The funds are held on the customer's card and nothing has been charged — Feature 3.8 Milestone F.
/// <para>
/// It carries the full snapshot its integration event needs (hard rule #9), so the handler that
/// publishes runs on the outbox, long after the placing request has gone, without reading the
/// payment back. The <c>pi_…</c> is deliberately <b>not</b> on it: no other service has any business
/// holding a provider identifier, and this event's whole audience is Orders.
/// </para>
/// </summary>
public sealed class PaymentAuthorizedDomainEvent(
    Guid paymentId,
    Guid orderId,
    Guid customerId,
    decimal amount,
    string currency,
    DateTime authorizedOnUtc) : DomainEvent
{
    public Guid PaymentId { get; init; } = paymentId;

    public Guid OrderId { get; init; } = orderId;

    public Guid CustomerId { get; init; } = customerId;

    public decimal Amount { get; init; } = amount;

    public string Currency { get; init; } = currency;

    public DateTime AuthorizedOnUtc { get; init; } = authorizedOnUtc;
}

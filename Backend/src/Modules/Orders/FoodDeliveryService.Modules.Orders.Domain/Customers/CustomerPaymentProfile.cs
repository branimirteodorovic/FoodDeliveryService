using FoodDeliveryService.Common.Domain;

namespace FoodDeliveryService.Modules.Orders.Domain.Customers;

/// <summary>
/// One fact about a customer, replicated from Payments: can this person be charged by card?
/// Feature 3.8 Milestone D, §6.3.
/// <para>
/// It is the whole of what Orders is told, and that is the design rather than a first cut. Orders
/// has no use for the Stripe identifiers — it could not call Stripe if it wanted to — and the brand
/// and last four digits are display fields belonging to the screen that manages cards, not to the
/// one that places an order. A replica that copies everything is a second database to keep correct
/// and a second place a card detail has to be scrubbed from.
/// </para>
/// <para>
/// <b>It avoids a call, it does not replace the check.</b> Payments authorizes against the card it
/// actually holds; this flag only stops Orders accepting an order that is certain to fail, and a
/// card removed a second ago is a flag that is briefly wrong (hard rule #9's trade — a stale replica
/// rather than a synchronous dependency).
/// </para>
/// <para>
/// Its own table rather than a column on <see cref="Customer"/>: the two are fed by different
/// services' events, and a card attached before this replica has seen the customer's registration
/// must still be recorded. Sharing the row would make the later event depend on the earlier one
/// having arrived.
/// </para>
/// </summary>
public sealed class CustomerPaymentProfile : Entity
{
    private CustomerPaymentProfile()
    {
    }

    /// <summary>The Users service's UserId — the same key Payments and <see cref="Customer"/> use.</summary>
    public Guid Id { get; private set; }

    public bool CanPayByCard { get; private set; }

    /// <summary>
    /// When the fact was last changed at the source, not when this row was written. Events can be
    /// redelivered and can arrive out of order, and this is what lets a late attach stop overwriting
    /// a newer detach.
    /// </summary>
    public DateTime ChangedOnUtc { get; private set; }

    public static CustomerPaymentProfile Create(Guid customerId, bool canPayByCard, DateTime changedOnUtc)
    {
        return new CustomerPaymentProfile
        {
            Id = customerId,
            CanPayByCard = canPayByCard,
            ChangedOnUtc = changedOnUtc
        };
    }

    /// <summary>
    /// Applies a later state. An event older than the one already applied is ignored rather than
    /// written: MassTransit gives no ordering guarantee across two messages, so an attach and a
    /// detach seconds apart can be delivered the wrong way round — and the wrong way round means a
    /// customer who removed their card keeps being offered it.
    /// </summary>
    public void Apply(bool canPayByCard, DateTime changedOnUtc)
    {
        if (changedOnUtc < ChangedOnUtc)
        {
            return;
        }

        CanPayByCard = canPayByCard;
        ChangedOnUtc = changedOnUtc;
    }
}

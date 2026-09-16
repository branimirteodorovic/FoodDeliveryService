namespace FoodDeliveryService.Modules.Orders.IntegrationEvents;

/// <summary>
/// The payment methods an order can carry, as the <em>contract</em> spells them — Feature 3.8
/// Milestone F.
/// <para>
/// <c>OrderPlacedIntegrationEvent.PaymentMethod</c> is a string rather than the
/// <c>Orders.Domain.PaymentMethod</c> enum, because hard rule #4 puts a module's Domain out of
/// every other module's reach and the <c>IntegrationEvents</c> project is the only shared surface.
/// The precedent is already here: <c>SupportTicketOpenedIntegrationEvent.Status</c> and
/// <c>UserRegisteredIntegrationEvent.Roles</c> travel the same way.
/// </para>
/// <para>
/// So the vocabulary is declared beside the contract that uses it, rather than as a literal
/// <c>"Card"</c> in Payments and another in Orders. Both sides compile against one constant, and
/// renaming the enum member without renaming this is a change that fails a test rather than one that
/// quietly stops every card order being charged.
/// </para>
/// </summary>
public static class OrderPaymentMethods
{
    /// <summary>Paid in cash to the courier. Nothing in Payments happens for these orders at all.</summary>
    public const string CashOnDelivery = nameof(CashOnDelivery);

    /// <summary>Charged to a card the customer saved earlier — authorized at placement (§1.3).</summary>
    public const string Card = nameof(Card);
}

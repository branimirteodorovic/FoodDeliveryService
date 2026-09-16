using FoodDeliveryService.Common.Application.Messaging;

namespace FoodDeliveryService.Modules.Payments.Application.Payments.AuthorizePayment;

/// <summary>
/// Place a hold on the customer's saved card for an order that was just placed — Feature 3.8
/// Milestone F, §1.3 step 3.
/// <para>
/// Driven only by <c>OrderPlacedIntegrationEvent</c> through the inbox, so it carries no validator:
/// its fields are a replicated snapshot that Orders validated at placement, and a rejection here
/// would not be a <c>400</c> to anybody — it would be a dropped replica
/// (<c>ValidatorCoverageTests</c> scopes itself to endpoint-reachable requests for exactly this
/// reason).
/// </para>
/// <para>
/// <b><see cref="Subtotal"/> is the amount charged, and it arrives on the event.</b> There is no fee
/// model to add to it (§0.4) and no reading it back out of Orders (hard rule #5) — the snapshot on
/// the event is the price the customer agreed to, computed server-side from the menu replica at
/// placement.
/// </para>
/// </summary>
public sealed record AuthorizePaymentCommand(
    Guid OrderId,
    Guid CustomerId,
    decimal Subtotal) : ICommand;

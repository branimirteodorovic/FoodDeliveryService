namespace FoodDeliveryService.Modules.Payments.Application.PaymentMethods.GetPaymentMethods;

/// <summary>
/// A saved card as a customer sees it. A response DTO, never the entity (hard rule #3) — and here
/// that rule does a second job: the entity carries the <c>pm_…</c> identifier and this does not.
/// <para>
/// Brand, last four and expiry are the whole of it, and that is the ceiling §0.5 sets rather than a
/// first iteration. There is no field here a PAN could be added next to without someone noticing.
/// </para>
/// </summary>
public sealed record PaymentMethodResponse
{
    /// <summary>This platform's identifier for the card — what the DELETE route takes.</summary>
    public Guid Id { get; init; }

    /// <summary>"visa", "mastercard", … — display only.</summary>
    public string? Brand { get; init; }

    public string? Last4 { get; init; }

    public int? ExpiryMonth { get; init; }

    public int? ExpiryYear { get; init; }

    public DateTime? AttachedOnUtc { get; init; }
}

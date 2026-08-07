namespace FoodDeliveryService.Modules.FraudDetection.Domain.Shared;

/// <summary>
/// The short-horizon counter window carried on <see cref="Customers.CustomerBehaviour"/>.
/// <para>
/// It is a <b>tumbling</b> window, not a sliding one: the counters advance until the window's age
/// exceeds <see cref="Length"/>, at which point they reset and a new window opens. That is what
/// makes it a projection update — O(1), no history scan on the event path — and it is deliberately
/// an approximation. The signals in Milestone B that need an exact sliding window derive it from
/// <see cref="Orders.OrderFact"/>, which retains every timestamp; this counter exists so a rate can
/// be read off a single row without touching the fact table at all.
/// </para>
/// <para>
/// The length is a domain default until Milestone B introduces <c>FraudOptions</c> and passes a
/// configured value into the entity methods, which already take it as a parameter.
/// </para>
/// </summary>
public static class BehaviourWindow
{
    public static readonly TimeSpan Length = TimeSpan.FromHours(24);
}

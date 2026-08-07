using FoodDeliveryService.Modules.FraudDetection.Domain.Customers;
using FoodDeliveryService.Modules.FraudDetection.Domain.Drivers;

namespace FoodDeliveryService.Modules.FraudDetection.Application.Behaviours;

/// <summary>
/// Get-or-create for the two behavioural projection rows.
/// <para>
/// Every handler in this module needs it, because FraudDetection never learns about a subject through a
/// single "created" event it can rely on: a customer may first appear on an order rather than a
/// registration, and a driver only ever appears mid-delivery. Centralising it keeps the
/// "first-seen" timestamp consistent — it is the arrival of the event that created the row, which
/// is the floor the account-age signals fall back to when the registration event never came.
/// </para>
/// </summary>
internal static class BehaviourProjections
{
    public static async Task<CustomerBehaviour> GetOrCreateAsync(
        this ICustomerBehavioursRepository repository,
        Guid customerId,
        DateTime firstSeenOnUtc,
        CancellationToken cancellationToken)
    {
        CustomerBehaviour? behaviour = await repository.GetAsync(customerId, cancellationToken);

        if (behaviour is not null)
        {
            return behaviour;
        }

        behaviour = CustomerBehaviour.Create(customerId, firstSeenOnUtc);

        repository.Insert(behaviour);

        return behaviour;
    }

    public static async Task<DriverBehaviour> GetOrCreateAsync(
        this IDriverBehavioursRepository repository,
        Guid driverId,
        DateTime firstSeenOnUtc,
        CancellationToken cancellationToken)
    {
        DriverBehaviour? behaviour = await repository.GetAsync(driverId, cancellationToken);

        if (behaviour is not null)
        {
            return behaviour;
        }

        behaviour = DriverBehaviour.Create(driverId, firstSeenOnUtc);

        repository.Insert(behaviour);

        return behaviour;
    }
}

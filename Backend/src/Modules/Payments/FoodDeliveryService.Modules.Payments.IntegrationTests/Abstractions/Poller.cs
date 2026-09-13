using FoodDeliveryService.Common.Domain;

namespace FoodDeliveryService.Modules.Payments.IntegrationTests.Abstractions;

/// <summary>
/// Waits for something the outbox and inbox jobs will get to eventually. Every cross-service
/// assertion in this suite goes through here: the publish, the broker hop and the projection are
/// three Quartz ticks apart, and asserting immediately after the HTTP call would only ever test how
/// fast the machine is.
/// </summary>
internal static class Poller
{
    private static readonly Error Timeout = Error.Failure("Poller.Timeout", "The poller has timed out");

    public static async Task<Result<T>> WaitAsync<T>(TimeSpan timeout, Func<Task<Result<T>>> action)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));

        DateTime endDate = DateTime.UtcNow.Add(timeout);

        while (DateTime.UtcNow < endDate && await timer.WaitForNextTickAsync())
        {
            Result<T> result = await action();

            if (result.IsSuccess)
            {
                return result;
            }
        }

        return Result.Failure<T>(Timeout);
    }
}

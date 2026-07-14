using FoodDeliveryService.Common.Domain;

namespace FoodDeliveryService.Modules.Restaurants.IntegrationTests.Abstractions;

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

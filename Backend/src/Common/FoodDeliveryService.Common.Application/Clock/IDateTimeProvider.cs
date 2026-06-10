namespace FoodDeliveryService.Common.Application.Clock;

public interface IDateTimeProvider
{
    DateTime UtcNow { get; }
}

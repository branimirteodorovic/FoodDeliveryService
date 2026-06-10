using FoodDeliveryService.Common.Application.Clock;

namespace FoodDeliveryService.Common.Infrastructure.Clock;

internal sealed class DateTimeProvider : IDateTimeProvider
{
    public DateTime UtcNow => DateTime.UtcNow;
}

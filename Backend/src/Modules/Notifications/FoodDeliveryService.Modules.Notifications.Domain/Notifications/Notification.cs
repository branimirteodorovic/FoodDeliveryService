using FoodDeliveryService.Common.Domain;

namespace FoodDeliveryService.Modules.Notifications.Domain.Notifications;

public sealed class Notification : Entity
{
    private Notification()
    {
    }

    public Guid Id { get; private set; }

    public static Notification Create(Guid id)
    {
        return new Notification
        {
            Id = id
        };
    }
}

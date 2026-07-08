using FoodDeliveryService.Common.Domain;

namespace FoodDeliveryService.Modules.Orders.Domain.Orders;

/// <summary>
/// Aggregate root for a customer order. All state changes go through guarded transition methods —
/// an illegal transition returns <see cref="OrderErrors.InvalidTransition"/>, never throws — and
/// every transition raises a domain event that feeds the outbox. Line prices, the subtotal and the
/// commission rate are server-side snapshots taken at placement; the idempotency key is unique in
/// the database so a retried placement returns the original order. OutForDelivery/Delivered are
/// driven by the Delivery service in Phase 2 — modeled here, not exposed via endpoints.
/// </summary>
public sealed class Order : Entity
{
    private readonly List<OrderItem> _items = [];

    private Order()
    {
    }

    public Guid Id { get; private set; }

    public Guid CustomerId { get; private set; }

    public Guid RestaurantId { get; private set; }

    public OrderStatus Status { get; private set; }

    public DeliveryAddress DeliveryAddress { get; private set; }

    public PaymentMethod PaymentMethod { get; private set; }

    public decimal Subtotal { get; private set; }

    // Snapshot from the Restaurant replica at placement — the payout math later must use the rate
    // that was in force when the order was placed, not the current one.
    public decimal CommissionRate { get; private set; }

    public string IdempotencyKey { get; private set; }

    public DateTime PlacedOnUtc { get; private set; }

    public IReadOnlyCollection<OrderItem> Items => _items.ToList();

    public static Result<Order> Place(
        Guid customerId,
        Guid restaurantId,
        DeliveryAddress deliveryAddress,
        PaymentMethod paymentMethod,
        IReadOnlyCollection<OrderLine> lines,
        decimal commissionRate,
        string idempotencyKey,
        DateTime utcNow)
    {
        if (lines.Count == 0)
        {
            return Result.Failure<Order>(OrderErrors.Empty);
        }

        var order = new Order
        {
            Id = Guid.NewGuid(),
            CustomerId = customerId,
            RestaurantId = restaurantId,
            Status = OrderStatus.Pending,
            DeliveryAddress = deliveryAddress,
            PaymentMethod = paymentMethod,
            CommissionRate = commissionRate,
            IdempotencyKey = idempotencyKey,
            PlacedOnUtc = utcNow
        };

        foreach (OrderLine line in lines)
        {
            order._items.Add(OrderItem.Create(order.Id, line));
        }

        order.Subtotal = order._items.Sum(item => item.LineTotal);

        order.Raise(new OrderPlacedDomainEvent(
            order.Id,
            order.CustomerId,
            order.RestaurantId,
            order.Subtotal,
            order.PlacedOnUtc));

        return order;
    }

    public Result Accept(DateTime utcNow)
    {
        Result result = Transition(OrderStatus.Accepted, OrderStatus.Pending);

        if (result.IsSuccess)
        {
            Raise(new OrderAcceptedDomainEvent(Id, CustomerId, RestaurantId, utcNow));
        }

        return result;
    }

    public Result Reject(string reason, DateTime utcNow)
    {
        Result result = Transition(OrderStatus.Rejected, OrderStatus.Pending);

        if (result.IsSuccess)
        {
            Raise(new OrderRejectedDomainEvent(Id, CustomerId, RestaurantId, reason, utcNow));
        }

        return result;
    }

    public Result StartPreparing()
    {
        Result result = Transition(OrderStatus.Preparing, OrderStatus.Accepted);

        if (result.IsSuccess)
        {
            Raise(new OrderPreparingDomainEvent(Id, CustomerId, RestaurantId));
        }

        return result;
    }

    public Result MarkReadyForPickup()
    {
        Result result = Transition(OrderStatus.ReadyForPickup, OrderStatus.Preparing);

        if (result.IsSuccess)
        {
            Raise(new OrderReadyForPickupDomainEvent(Id, CustomerId, RestaurantId));
        }

        return result;
    }

    // Customers may back out until the restaurant starts preparing the food.
    public Result Cancel(DateTime utcNow)
    {
        Result result = Transition(OrderStatus.Cancelled, OrderStatus.Pending, OrderStatus.Accepted);

        if (result.IsSuccess)
        {
            Raise(new OrderCancelledDomainEvent(Id, CustomerId, RestaurantId, utcNow));
        }

        return result;
    }

    public Result MarkOutForDelivery()
    {
        Result result = Transition(OrderStatus.OutForDelivery, OrderStatus.ReadyForPickup);

        if (result.IsSuccess)
        {
            Raise(new OrderOutForDeliveryDomainEvent(Id, CustomerId, RestaurantId));
        }

        return result;
    }

    public Result MarkDelivered(DateTime utcNow)
    {
        Result result = Transition(OrderStatus.Delivered, OrderStatus.OutForDelivery);

        if (result.IsSuccess)
        {
            Raise(new OrderDeliveredDomainEvent(Id, CustomerId, RestaurantId, utcNow));
        }

        return result;
    }

    private Result Transition(OrderStatus to, params OrderStatus[] from)
    {
        if (!from.Contains(Status))
        {
            return Result.Failure(OrderErrors.InvalidTransition(Status, to));
        }

        Status = to;

        return Result.Success();
    }
}

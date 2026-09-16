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

    /// <summary>
    /// Where the money is — a second, independent dimension beside <see cref="Status"/>, and a
    /// projection of the Payments service's state rather than a source of truth (§1.2). The one
    /// decision it drives here is the guard in <see cref="Accept"/>.
    /// </summary>
    public PaymentStatus PaymentStatus { get; private set; }

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
            // A card order is placed already waiting on its hold; a cash order never waits on one.
            // Set here rather than defaulted, because "which dimension applies to this order" is
            // decided by the payment method and by nothing that happens later.
            PaymentStatus = paymentMethod == PaymentMethod.Card
                ? PaymentStatus.Authorizing
                : PaymentStatus.NotRequired,
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
            order.PaymentMethod,
            order.PlacedOnUtc));

        return order;
    }

    /// <summary>
    /// The restaurant takes the order on. Feature 3.8 Milestone F, §8.2: a card order may not be
    /// accepted until its hold is actually on the card.
    /// <para>
    /// The payment guard runs <b>before</b> the status transition, so a restaurant accepting inside
    /// the authorization window — typically under a second — gets a clean, retryable
    /// <c>PaymentNotAuthorized</c> rather than a generic invalid-transition error. The corollary is
    /// that a card order whose payment failed answers the same way instead of "already cancelled",
    /// which is the less informative of the two and is accepted deliberately: the reason it cannot
    /// be accepted really is the payment.
    /// </para>
    /// </summary>
    public Result Accept(DateTime utcNow)
    {
        if (PaymentMethod == PaymentMethod.Card && PaymentStatus != PaymentStatus.Authorized)
        {
            return Result.Failure(OrderErrors.PaymentNotAuthorized);
        }

        OrderStatus previousStatus = Status;

        Result result = Transition(OrderStatus.Accepted, OrderStatus.Pending);

        if (result.IsSuccess)
        {
            Raise(new OrderAcceptedDomainEvent(Id, CustomerId, RestaurantId, previousStatus, utcNow));
        }

        return result;
    }

    public Result Reject(string reason, DateTime utcNow)
    {
        OrderStatus previousStatus = Status;

        Result result = Transition(OrderStatus.Rejected, OrderStatus.Pending);

        if (result.IsSuccess)
        {
            Raise(new OrderRejectedDomainEvent(Id, CustomerId, RestaurantId, previousStatus, reason, utcNow));
        }

        return result;
    }

    public Result StartPreparing()
    {
        OrderStatus previousStatus = Status;

        Result result = Transition(OrderStatus.Preparing, OrderStatus.Accepted);

        if (result.IsSuccess)
        {
            Raise(new OrderPreparingDomainEvent(Id, CustomerId, RestaurantId, previousStatus));
        }

        return result;
    }

    public Result MarkReadyForPickup()
    {
        OrderStatus previousStatus = Status;

        Result result = Transition(OrderStatus.ReadyForPickup, OrderStatus.Preparing);

        if (result.IsSuccess)
        {
            Raise(new OrderReadyForPickupDomainEvent(Id, CustomerId, RestaurantId, previousStatus));
        }

        return result;
    }

    // Customers may back out until the restaurant starts preparing the food.
    public Result Cancel(DateTime utcNow)
    {
        OrderStatus previousStatus = Status;

        Result result = Transition(OrderStatus.Cancelled, OrderStatus.Pending, OrderStatus.Accepted);

        if (result.IsSuccess)
        {
            Raise(new OrderCancelledDomainEvent(Id, CustomerId, RestaurantId, previousStatus, utcNow));
        }

        return result;
    }

    public Result MarkOutForDelivery()
    {
        OrderStatus previousStatus = Status;

        Result result = Transition(OrderStatus.OutForDelivery, OrderStatus.ReadyForPickup);

        if (result.IsSuccess)
        {
            Raise(new OrderOutForDeliveryDomainEvent(Id, CustomerId, RestaurantId, previousStatus));
        }

        return result;
    }

    public Result MarkDelivered(DateTime utcNow)
    {
        OrderStatus previousStatus = Status;

        Result result = Transition(OrderStatus.Delivered, OrderStatus.OutForDelivery);

        if (result.IsSuccess)
        {
            Raise(new OrderDeliveredDomainEvent(Id, CustomerId, RestaurantId, previousStatus, utcNow));
        }

        return result;
    }

    /// <summary>
    /// Records that the hold is on the card — projected from <c>PaymentAuthorizedIntegrationEvent</c>.
    /// <para>
    /// Raises nothing: this is a projection of another service's state, and an event here would be
    /// Orders announcing news that Payments already announced. It is a no-op from any status but
    /// <see cref="PaymentStatus.Authorizing"/>, which covers both the redelivery the inbox is
    /// entitled to make and the authorization that arrives after a capture has already been
    /// projected — messages carry no ordering with respect to each other.
    /// </para>
    /// </summary>
    public Result MarkPaymentAuthorized()
    {
        if (PaymentMethod != PaymentMethod.Card)
        {
            return Result.Failure(OrderErrors.PaymentNotRequired);
        }

        if (PaymentStatus != PaymentStatus.Authorizing)
        {
            return Result.Success();
        }

        PaymentStatus = PaymentStatus.Authorized;

        return Result.Success();
    }

    /// <summary>
    /// The card was refused, so the order ends — Feature 3.8 Milestone F, §8.3. Cancels the order
    /// and raises <see cref="OrderPaymentFailedDomainEvent"/> rather than reusing
    /// <see cref="Cancel"/>: see that event for why the distinction is load-bearing rather than
    /// cosmetic.
    /// <para>
    /// Two tolerances, both because the inbox dispatches at least once and messages are unordered.
    /// A second delivery finds the payment already <see cref="PaymentStatus.Failed"/> and does
    /// nothing. And an order the customer cancelled while the authorization was still in flight is
    /// already where a failed payment would put it, so the payment outcome is recorded without a
    /// second cancellation event.
    /// </para>
    /// </summary>
    public Result FailPayment(string reason, DateTime utcNow)
    {
        if (PaymentMethod != PaymentMethod.Card)
        {
            return Result.Failure(OrderErrors.PaymentNotRequired);
        }

        if (PaymentStatus == PaymentStatus.Failed)
        {
            return Result.Success();
        }

        if (Status == OrderStatus.Cancelled)
        {
            PaymentStatus = PaymentStatus.Failed;

            return Result.Success();
        }

        OrderStatus previousStatus = Status;

        Result result = Transition(OrderStatus.Cancelled, OrderStatus.Pending);

        if (result.IsFailure)
        {
            return result;
        }

        PaymentStatus = PaymentStatus.Failed;

        Raise(new OrderPaymentFailedDomainEvent(
            Id,
            CustomerId,
            RestaurantId,
            previousStatus,
            reason,
            utcNow));

        return Result.Success();
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

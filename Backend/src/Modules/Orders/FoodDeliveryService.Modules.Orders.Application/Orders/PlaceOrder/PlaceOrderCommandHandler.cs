using FoodDeliveryService.Common.Application.Messaging;
using FoodDeliveryService.Common.Domain;
using FoodDeliveryService.Modules.Orders.Application.Abstractions.Authentication;
using FoodDeliveryService.Modules.Orders.Application.Abstractions.Data;
using FoodDeliveryService.Modules.Orders.Domain.Customers;
using FoodDeliveryService.Modules.Orders.Domain.Orders;
using FoodDeliveryService.Modules.Orders.Domain.Restaurants;

namespace FoodDeliveryService.Modules.Orders.Application.Orders.PlaceOrder;

internal sealed class PlaceOrderCommandHandler(
    IOrdersContext ordersContext,
    IOrdersRepository ordersRepository,
    ICustomerRepository customerRepository,
    IRestaurantReplicaRepository restaurantReplicaRepository,
    IMenuItemReplicaRepository menuItemReplicaRepository,
    IUnitOfWork unitOfWork)
    : ICommandHandler<PlaceOrderCommand, Guid>
{
    public async Task<Result<Guid>> Handle(PlaceOrderCommand request, CancellationToken cancellationToken)
    {
        // A retried placement with a key that already produced an order returns that order.
        Order? existingOrder = await ordersRepository.GetByIdempotencyKeyAsync(
            request.IdempotencyKey,
            cancellationToken);

        if (existingOrder is not null)
        {
            return existingOrder.Id;
        }

        Guid customerId = ordersContext.UserId;

        Customer? customer = await customerRepository.GetAsync(customerId, cancellationToken);

        if (customer is null)
        {
            return Result.Failure<Guid>(OrderErrors.CustomerNotFound(customerId));
        }

        Restaurant? restaurant = await restaurantReplicaRepository.GetAsync(
            request.RestaurantId,
            cancellationToken);

        if (restaurant is null)
        {
            return Result.Failure<Guid>(OrderErrors.RestaurantNotFound(request.RestaurantId));
        }

        Result<DeliveryAddress> deliveryAddressResult = DeliveryAddress.Create(
            request.Street,
            request.City,
            request.PostalCode,
            request.Country,
            request.Notes,
            request.Latitude,
            request.Longitude);

        if (deliveryAddressResult.IsFailure)
        {
            return Result.Failure<Guid>(deliveryAddressResult.Error);
        }

        Result<List<OrderLine>> linesResult = await PriceLinesFromReplicaAsync(request, cancellationToken);

        if (linesResult.IsFailure)
        {
            return Result.Failure<Guid>(linesResult.Error);
        }

        Result<Order> orderResult = Order.Place(
            customerId,
            request.RestaurantId,
            deliveryAddressResult.Value,
            Enum.Parse<PaymentMethod>(request.PaymentMethod, ignoreCase: true),
            linesResult.Value,
            restaurant.CommissionRate,
            request.IdempotencyKey,
            DateTime.UtcNow);

        if (orderResult.IsFailure)
        {
            return Result.Failure<Guid>(orderResult.Error);
        }

        ordersRepository.Insert(orderResult.Value);

        try
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (Exception)
        {
            // Two concurrent requests with the same key: the loser hits the unique index on
            // idempotency_key. If the winning order is now visible, return it — same outcome as
            // the lookup at the top. Anything else was a real failure, so rethrow.
            Order? winningOrder = await ordersRepository.GetByIdempotencyKeyAsync(
                request.IdempotencyKey,
                cancellationToken);

            if (winningOrder is not null && winningOrder.Id != orderResult.Value.Id)
            {
                return winningOrder.Id;
            }

            throw;
        }

        return orderResult.Value.Id;
    }

    // Server-side pricing: every line is priced from the local MenuItem replica; ids that are not
    // on this restaurant's menu (or not currently available) reject the whole placement.
    private async Task<Result<List<OrderLine>>> PriceLinesFromReplicaAsync(
        PlaceOrderCommand request,
        CancellationToken cancellationToken)
    {
        IReadOnlyCollection<MenuItem> menuItems = await menuItemReplicaRepository.GetManyAsync(
            request.RestaurantId,
            request.Items.Select(i => i.MenuItemId).Distinct().ToArray(),
            cancellationToken);

        var menuItemsById = menuItems.ToDictionary(m => m.Id);

        var lines = new List<OrderLine>(request.Items.Count);

        foreach (PlaceOrderItem item in request.Items)
        {
            if (!menuItemsById.TryGetValue(item.MenuItemId, out MenuItem? menuItem))
            {
                return Result.Failure<List<OrderLine>>(OrderErrors.MenuItemNotFound(item.MenuItemId));
            }

            if (!menuItem.IsAvailable)
            {
                return Result.Failure<List<OrderLine>>(OrderErrors.MenuItemUnavailable(item.MenuItemId));
            }

            lines.Add(new OrderLine(menuItem.Id, menuItem.Name, menuItem.Price, item.Quantity));
        }

        return lines;
    }
}

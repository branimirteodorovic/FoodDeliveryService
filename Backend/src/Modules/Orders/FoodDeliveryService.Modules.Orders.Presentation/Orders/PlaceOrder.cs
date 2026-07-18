using FoodDeliveryService.Common.Domain;
using FoodDeliveryService.Common.Presentation.Endpoints;
using FoodDeliveryService.Common.Presentation.Results;
using FoodDeliveryService.Modules.Orders.Application;
using FoodDeliveryService.Modules.Orders.Application.Orders.PlaceOrder;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace FoodDeliveryService.Modules.Orders.Presentation.Orders;

internal sealed class PlaceOrder : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("orders", async (
            Request request,
            [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
            ISender sender,
            CancellationToken cancellationToken) =>
        {
            var command = new PlaceOrderCommand(
                request.RestaurantId,
                request.Items.Select(i => new PlaceOrderItem(i.MenuItemId, i.Quantity)).ToArray(),
                request.DeliveryAddress.Street,
                request.DeliveryAddress.City,
                request.DeliveryAddress.PostalCode,
                request.DeliveryAddress.Country,
                request.DeliveryAddress.Notes,
                request.DeliveryAddress.Latitude,
                request.DeliveryAddress.Longitude,
                request.PaymentMethod,
                idempotencyKey ?? string.Empty);

            Result<Guid> result = await sender.Send(command, cancellationToken);

            return result.Match(Results.Ok, ApiResults.Problem);
        })
        .RequireAuthorization(Permissions.CreateOrder)
        .WithTags(Tags.Orders);
    }

    internal sealed class Request
    {
        public Guid RestaurantId { get; init; }

        // Ids + quantities only — the server prices every line from its menu replica.
        public IReadOnlyCollection<ItemRequest> Items { get; init; } = [];

        public AddressRequest DeliveryAddress { get; init; } = new();

        public string PaymentMethod { get; init; } = nameof(Domain.Orders.PaymentMethod.CashOnDelivery);
    }

    internal sealed class ItemRequest
    {
        public Guid MenuItemId { get; init; }

        public int Quantity { get; init; }
    }

    internal sealed class AddressRequest
    {
        public string Street { get; init; } = string.Empty;

        public string City { get; init; } = string.Empty;

        public string PostalCode { get; init; } = string.Empty;

        public string Country { get; init; } = string.Empty;

        public string? Notes { get; init; }

        public double? Latitude { get; init; }

        public double? Longitude { get; init; }
    }
}

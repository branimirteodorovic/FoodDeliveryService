using System.Data.Common;
using Dapper;
using FoodDeliveryService.Common.Application.Clock;
using FoodDeliveryService.Common.Application.Data;
using FoodDeliveryService.Common.Application.Messaging;
using FoodDeliveryService.Common.Domain;
using FoodDeliveryService.Modules.Delivery.Application.Abstractions.Authentication;
using FoodDeliveryService.Modules.Delivery.Domain.Deliveries;

namespace FoodDeliveryService.Modules.Delivery.Application.Deliveries.GetMyDeliveryOffers;

/// <summary>
/// Reads the caller's live offers. The scope is the same <see cref="DeliveryAccess.LiveOfferSql"/>
/// predicate the detail reads widened onto, so "an offer I may open by id" and "an offer on my
/// list" cannot come to mean different things.
/// </summary>
internal sealed class GetMyDeliveryOffersQueryHandler(
    IDbConnectionFactory dbConnectionFactory,
    IDeliveryContext deliveryContext,
    IDateTimeProvider dateTimeProvider)
    : IQueryHandler<GetMyDeliveryOffersQuery, IReadOnlyCollection<DeliveryOfferResponse>>
{
    public async Task<Result<IReadOnlyCollection<DeliveryOfferResponse>>> Handle(
        GetMyDeliveryOffersQuery request,
        CancellationToken cancellationToken)
    {
        await using DbConnection connection = await dbConnectionFactory.OpenConnectionAsync();

        // The already-lapsed offers are excluded here rather than returned for the client to filter:
        // ProcessExpiredOffersJob clears them on its own tick, so a driver polling in between would
        // otherwise be shown an offer the accept endpoint will refuse — and the deadline is the only
        // thing that makes that call correctly from either side.
        //
        // Soonest deadline first: the offer screen's ordering is "what lapses next", not "what
        // arrived first" — two offers made seconds apart can carry the same window, and the one
        // about to expire is the one the driver has to answer.
        const string sql =
            $"""
             SELECT
                 d.id AS {nameof(DeliveryOfferResponse.Id)},
                 d.order_id AS {nameof(DeliveryOfferResponse.OrderId)},
                 d.restaurant_id AS {nameof(DeliveryOfferResponse.RestaurantId)},
                 d.pickup_latitude AS {nameof(DeliveryOfferResponse.PickupLatitude)},
                 d.pickup_longitude AS {nameof(DeliveryOfferResponse.PickupLongitude)},
                 d.dropoff_street AS {nameof(DeliveryOfferResponse.DropoffStreet)},
                 d.dropoff_city AS {nameof(DeliveryOfferResponse.DropoffCity)},
                 d.dropoff_postal_code AS {nameof(DeliveryOfferResponse.DropoffPostalCode)},
                 d.dropoff_country AS {nameof(DeliveryOfferResponse.DropoffCountry)},
                 d.dropoff_notes AS {nameof(DeliveryOfferResponse.DropoffNotes)},
                 d.dropoff_latitude AS {nameof(DeliveryOfferResponse.DropoffLatitude)},
                 d.dropoff_longitude AS {nameof(DeliveryOfferResponse.DropoffLongitude)},
                 d.offer_expires_on_utc AS {nameof(DeliveryOfferResponse.OfferExpiresOnUtc)},
                 d.created_on_utc AS {nameof(DeliveryOfferResponse.CreatedOnUtc)}
             FROM deliveries d
             WHERE d.status = @OfferedStatus AND {DeliveryAccess.LiveOfferSql}
             ORDER BY d.offer_expires_on_utc
             """;

        IEnumerable<DeliveryOfferResponse> offers = await connection.QueryAsync<DeliveryOfferResponse>(
            sql,
            new
            {
                deliveryContext.UserId,
                dateTimeProvider.UtcNow,
                OfferedStatus = (int)DeliveryStatus.Offered
            });

        return offers.ToList();
    }
}

using FoodDeliveryService.Common.Application.Messaging;

namespace FoodDeliveryService.Modules.Delivery.Application.Deliveries.GetMyDeliveryOffers;

/// <summary>
/// The calling driver's outstanding delivery offers — the read behind the offer screen.
/// <para>
/// No parameters and no administrator bypass: the driver is the JWT subject, and a driver id here
/// would let one driver enumerate another's offers. A <b>collection</b> rather than a single offer,
/// because <c>Driver.Reserve()</c> only runs on accept — a driver stays <c>Available</c> while an
/// offer is outstanding, so the assignment routine can legitimately offer them a second delivery
/// before they have answered the first.
/// </para>
/// <para>
/// Unpaged for the same reason <c>GetPaymentMethodsQuery</c> is: the set is bounded by how many
/// deliveries happen to be mid-offer to one driver at one instant — a handful at most, and each
/// row self-expires within the offer window.
/// </para>
/// </summary>
public sealed record GetMyDeliveryOffersQuery : IQuery<IReadOnlyCollection<DeliveryOfferResponse>>;

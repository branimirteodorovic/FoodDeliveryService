namespace FoodDeliveryService.Modules.RealTime.Application.RealTime;

/// <summary>
/// The server→client payload for the <c>DeliveryOffered</c> hub method — the nudge that tells a
/// driver an offer is waiting. Part of the feature's public API; keep it additive-only.
/// <para>
/// <b>The client is expected to call <c>GET delivery/drivers/me/offers</c> on receipt</b> and
/// render from that. This frame deliberately carries no pickup or drop-off detail: the socket is
/// best-effort and the REST read model is authoritative, so a driver who was offline for the frame
/// and a driver who received it must arrive at the same screen by the same call. Everything here is
/// only what the client needs to decide whether that call is worth making, and to match the result
/// against an offer it may already be showing.
/// </para>
/// <para>
/// <c>OfferExpiresOnUtc</c> is also the client's <b>self-expiry</b> clock. No retraction frame is
/// ever sent — see <c>DeliveryOfferedConsumer</c>.
/// </para>
/// </summary>
public sealed record DeliveryOfferFrame(
    Guid DeliveryId,
    Guid OrderId,
    DateTime OfferExpiresOnUtc);

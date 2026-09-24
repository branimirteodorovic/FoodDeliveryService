namespace FoodDeliveryService.Modules.Users.Application.Users.GetCurrentUser;

/// <summary>
/// Who the caller is, as the client needs it for routing and for deciding what to render.
/// <para>
/// Distinct from <c>UserResponse</c> in two ways that matter. It carries <see cref="Roles"/>, which
/// exists nowhere in the access token — <c>ApiResource("fooddeliveryservice.api")</c> declares no
/// <c>UserClaims</c>, so roles never leave this module's database unless something asks for them.
/// And its id field is named <see cref="UserId"/> rather than <c>Id</c>, because the one mistake a
/// client can make here is to reach for the token's <c>sub</c> instead: that is the Duende identity
/// id, and it matches nothing — not <c>OrderResponse.CustomerId</c>, not the SignalR
/// <c>user:{id}</c> group.
/// </para>
/// <para>
/// This is <b>UI convenience, not a security boundary.</b> Nothing downstream trusts a role the
/// client read here; every permission is still resolved server-side per request by
/// <c>CustomClaimsTransformation</c> and enforced by <c>PermissionAuthorizationHandler</c>.
/// </para>
/// </summary>
public sealed record CurrentUserResponse(
    Guid UserId,
    string Email,
    string FirstName,
    string LastName,
    IReadOnlyCollection<string> Roles);

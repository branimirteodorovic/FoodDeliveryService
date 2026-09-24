using FoodDeliveryService.Common.Application.Messaging;

namespace FoodDeliveryService.Modules.Users.Application.Users.GetCurrentUser;

/// <summary>
/// The calling user's own profile and roles — the read behind "who am I signed in as".
/// <para>
/// No parameters, and not a reuse of <c>GetUserQuery(Guid)</c>, which takes an id and is what a
/// lookup of <em>somebody else</em> would be built on. Widening that one to also carry roles would
/// put a role list behind an id the caller supplies; keeping them separate means this slice can
/// never be asked about an account other than the JWT subject's.
/// </para>
/// </summary>
public sealed record GetCurrentUserQuery : IQuery<CurrentUserResponse>;

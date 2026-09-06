using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;

namespace FoodDeliveryService.Common.Presentation.Documentation;

/// <summary>
/// Closes the documentation surface to anonymous callers outside Development — Feature 3.7
/// Milestone G §8.3.
/// <para>
/// Mapping the UI in every environment (rather than only in Development, as the hosts used to) is
/// what makes the documented surface visible from the one place the API is actually reachable. It is
/// also what would otherwise publish a complete, machine-readable inventory of every route, every
/// parameter and every permission on the platform to anyone who can reach the port — free
/// reconnaissance, and the kind that stays accurate.
/// </para>
/// <para>
/// The bar is <b>authentication</b>, not a permission. There is no <c>docs:read</c> in the seeded
/// permission set, and inventing one would put a Users migration in the way of a documentation
/// change; "hold a token this platform issued" is the honest line and it is the one that keeps the
/// schema off the open internet. The endpoints themselves remain individually authorized regardless
/// — reading about <c>POST orders/{id}/accept</c> has never been the same thing as being allowed to
/// call it.
/// </para>
/// </summary>
internal sealed class ApiDocumentationAuthorizationMiddleware(
    RequestDelegate next,
    ApiDocumentationDescriptor descriptor)
{
    public async Task InvokeAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (descriptor.Covers(context.Request.Path.Value) &&
            context.User.Identity?.IsAuthenticated != true)
        {
            // ChallengeAsync rather than a bare 401, so the response carries the WWW-Authenticate
            // header the scheme defines and looks like every other unauthenticated response here.
            await context.ChallengeAsync().ConfigureAwait(false);

            return;
        }

        await next(context).ConfigureAwait(false);
    }
}

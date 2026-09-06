using System.Globalization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace FoodDeliveryService.Common.Presentation.Documentation;

/// <summary>
/// Puts each operation's authorization into the document — the permission it needs, the security
/// requirement that says a bearer token is expected at all, and the two responses that follow from
/// getting either wrong.
/// </summary>
/// <remarks>
/// This is read straight off the endpoint metadata rather than written by hand, which is the only
/// version of it that stays true: <c>EndpointAuthorizationTests</c> already fails the build for an
/// endpoint whose policy names a permission the Users module never seeds, so the string this
/// transformer prints is a string that has been checked. A hand-written "requires orders:create" in
/// a summary is checked by nobody.
/// </remarks>
internal sealed class AuthorizationOperationTransformer : IOpenApiOperationTransformer
{
    public Task TransformAsync(
        OpenApiOperation operation,
        OpenApiOperationTransformerContext context,
        CancellationToken cancellationToken)
    {
        IList<object> metadata = context.Description.ActionDescriptor.EndpointMetadata;

        if (metadata.OfType<IAllowAnonymous>().Any())
        {
            // Anonymous on purpose (registration, invitation acceptance) — and worth saying so,
            // because "no security requirement" reads identically to "nobody documented it".
            operation.Description = Append(
                operation.Description,
                "**Anonymous.** No bearer token is required — this endpoint is reachable before the " +
                "caller has one.");

            return Task.CompletedTask;
        }

        string[] permissions =
        [
            .. metadata
                .OfType<IAuthorizeData>()
                .Select(authorization => authorization.Policy)
                .Where(policy => !string.IsNullOrWhiteSpace(policy))
                .Select(policy => policy!)
                .Distinct(StringComparer.Ordinal)
        ];

        if (metadata.OfType<IAuthorizeData>().Any())
        {
            operation.Security ??= [];

            operation.Security.Add(new OpenApiSecurityRequirement
            {
                [new OpenApiSecuritySchemeReference(ApiDocumentTransformer.BearerSchemeName, context.Document)] = []
            });

            operation.Description = Append(
                operation.Description,
                permissions.Length > 0
                    ? $"**Requires permission:** `{string.Join("`, `", permissions)}`."
                    : "**Requires authentication.** Any authenticated caller; the handler scopes what " +
                      "it returns to that caller.");

            operation.Responses ??= [];

            operation.Responses.TryAdd(
                Status(StatusCodes.Status401Unauthorized),
                new OpenApiResponse { Description = "No bearer token, or a token that failed validation." });

            operation.Responses.TryAdd(
                Status(StatusCodes.Status403Forbidden),
                new OpenApiResponse
                {
                    Description = permissions.Length > 0
                        ? "Authenticated, but the caller does not hold the permission above."
                        : "Authenticated, but not authorized for this resource."
                });
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Adds a paragraph to an operation description without clobbering the <c>WithDescription</c>
    /// the endpoint wrote. Both halves matter: the endpoint says what the call does, this says what
    /// it takes to make it.
    /// </summary>
    internal static string Append(string? description, string paragraph) =>
        string.IsNullOrWhiteSpace(description) ? paragraph : $"{description}\n\n{paragraph}";

    internal static string Status(int statusCode) => statusCode.ToString(CultureInfo.InvariantCulture);
}

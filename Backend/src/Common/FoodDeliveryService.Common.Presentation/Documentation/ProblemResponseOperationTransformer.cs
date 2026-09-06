using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace FoodDeliveryService.Common.Presentation.Documentation;

/// <summary>
/// Declares the failure half of every operation — the RFC 7807 responses <c>ApiResults.Problem</c>
/// can actually produce — without a single <c>.Produces&lt;ProblemDetails&gt;</c> at a call site.
/// <para>
/// The error surface of this platform is uniform by construction: handlers return
/// <c>Result&lt;T&gt;</c>, the endpoint calls <c>result.Match(Results.Ok, ApiResults.Problem)</c>,
/// and that one function maps <c>ErrorType</c> to 400 / 404 / 409 / 500. Writing those four out on
/// each of ~50 endpoints would be fifty chances to write them differently; deriving them from the
/// verb once is fifty operations that cannot disagree.
/// </para>
/// </summary>
/// <remarks>
/// 429 is here for a different reason than the rest: nothing in the service produces it. The
/// Gateway's edge rate limiter does (<c>docs/rate-limiting.md</c>), and a client reading the
/// service's document is still a client that will meet it, because every client reaches the service
/// through the Gateway. Documenting it only on the Gateway — which serves no document of its own —
/// would document it nowhere.
/// </remarks>
internal sealed class ProblemResponseOperationTransformer : IOpenApiOperationTransformer
{
    private const string ProblemContentType = "application/problem+json";

    public Task TransformAsync(
        OpenApiOperation operation,
        OpenApiOperationTransformerContext context,
        CancellationToken cancellationToken)
    {
        operation.Responses ??= [];

        // A reference to the one component ApiDocumentTransformer registers, not a fresh inline
        // copy of the schema on every response.
        var problemDetails = new OpenApiSchemaReference(
            ApiDocumentTransformer.ProblemDetailsSchemaName,
            context.Document);

        // Validation is the one every operation can return: the FluentValidation behaviour runs
        // before every handler, and Feature 3.7 Milestone F made a validator mandatory for every
        // request an endpoint can reach.
        Add(StatusCodes.Status400BadRequest,
            "The request failed validation, or the command was rejected as a business rule violation. " +
            "Validation failures carry an `errors` extension member keyed by property name.");

        // Anything addressing a resource by id, which is most of the surface. It is also what a
        // write-side ownership failure returns rather than a 403 (Milestone F §7.4): a 403 would
        // confirm that somebody else's order exists.
        if (AddressesAResource(context))
        {
            Add(StatusCodes.Status404NotFound,
                "No such resource — or one the caller is not entitled to see. The two are deliberately " +
                "indistinguishable.");
        }

        // A state machine refusing a transition: cancelling a delivered order, claiming a ticket
        // another agent already holds, approving a refund the requesting agent approved themselves.
        if (Mutates(context))
        {
            Add(StatusCodes.Status409Conflict,
                "The resource is not in a state that allows this transition.");
        }

        Add(StatusCodes.Status429TooManyRequests,
            "Shed by the Gateway's edge rate limiter. Carries `Retry-After`; retry after that many seconds.");

        Add(StatusCodes.Status500InternalServerError,
            "An unhandled failure. The body carries no detail on purpose — the correlation id on the " +
            "`X-Correlation-Id` response header is what finds the log line.");

        return Task.CompletedTask;

        void Add(int statusCode, string description) =>
            operation.Responses.TryAdd(
                AuthorizationOperationTransformer.Status(statusCode),
                new OpenApiResponse
                {
                    Description = description,
                    Content = new Dictionary<string, OpenApiMediaType>(StringComparer.Ordinal)
                    {
                        [ProblemContentType] = new() { Schema = problemDetails }
                    }
                });
    }

    /// <summary>Whether the route takes an id — the precondition for there being nothing to find.</summary>
    private static bool AddressesAResource(OpenApiOperationTransformerContext context) =>
        context.Description.ParameterDescriptions.Any(parameter =>
            string.Equals(parameter.Source.Id, "Path", StringComparison.Ordinal));

    private static bool Mutates(OpenApiOperationTransformerContext context) =>
        !HttpMethods.IsGet(context.Description.HttpMethod ?? string.Empty) &&
        !HttpMethods.IsHead(context.Description.HttpMethod ?? string.Empty);
}

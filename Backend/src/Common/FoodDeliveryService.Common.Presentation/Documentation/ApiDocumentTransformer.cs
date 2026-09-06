using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace FoodDeliveryService.Common.Presentation.Documentation;

/// <summary>
/// Fills in what a service says about itself once per document: the title and description from its
/// <see cref="ApiDocumentationDescriptor"/>, and the one security scheme every authorized operation
/// references.
/// </summary>
/// <remarks>
/// The scheme is declared here rather than on each operation because it is a component: sixty
/// operations pointing at one <c>bearerAuth</c> definition is the difference between a document a
/// reader can use and a document that repeats the same paragraph sixty times.
/// </remarks>
internal sealed class ApiDocumentTransformer(
    ApiDocumentationDescriptor descriptor,
    ApiDocumentationOptions options) : IOpenApiDocumentTransformer
{
    /// <summary>
    /// The component name every operation's security requirement points at. Referenced by
    /// <see cref="AuthorizationOperationTransformer"/> and asserted by the completeness test, so it
    /// lives in one place.
    /// </summary>
    internal const string BearerSchemeName = "bearerAuth";

    /// <summary>
    /// The component name every failure response points at. Registered once here rather than
    /// inlined per response by <see cref="ProblemResponseOperationTransformer"/>: the same schema on
    /// six responses across fifty operations is three hundred copies of it in one document, and a
    /// reader scrolling past them is a reader who stops reading.
    /// </summary>
    internal const string ProblemDetailsSchemaName = "ProblemDetails";

    public async Task TransformAsync(
        OpenApiDocument document,
        OpenApiDocumentTransformerContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(context);

        document.Info = new OpenApiInfo
        {
            Title = descriptor.Title,
            Version = options.Version,
            Description = descriptor.Description,
            Contact = new OpenApiContact
            {
                Name = options.ContactName,
                Url = Uri.TryCreate(options.ContactUrl, UriKind.Absolute, out Uri? url) ? url : null
            }
        };

        document.Components ??= new OpenApiComponents();
        document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>(StringComparer.Ordinal);

        document.Components.SecuritySchemes[BearerSchemeName] = new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.Http,
            Scheme = "bearer",
            BearerFormat = "JWT",
            Description =
                "A JWT issued by the platform's Duende IdentityServer. Every service validates it a " +
                "second time behind the Gateway and then resolves the caller's permissions from the " +
                "Users service, so a token that authenticates is not by itself a token that authorizes " +
                "— see the permission named on each operation. `docs/api-documentation.md` has the " +
                "password-grant call that gets you one."
        };

        document.Components.Schemas ??= new Dictionary<string, IOpenApiSchema>(StringComparer.Ordinal);

        document.Components.Schemas[ProblemDetailsSchemaName] = await context
            .GetOrCreateSchemaAsync(typeof(ProblemDetails), parameterDescription: null, cancellationToken)
            .ConfigureAwait(false);
    }
}

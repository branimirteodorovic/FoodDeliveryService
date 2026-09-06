namespace FoodDeliveryService.Common.Presentation.Documentation;

/// <summary>
/// What one service says about itself in its OpenAPI document, and where that document and the two
/// UIs over it live.
/// <para>
/// The seven hosts used to carry seven copies of a <c>SwaggerExtensions</c> that titled every one of
/// them "FoodDeliveryService API … built using the modular monolith architecture" — a string that
/// was both identical across services and wrong about the architecture. The title is per-service
/// because the documents are per-service: there is no aggregated document to write, and pretending
/// otherwise is how a reader ends up believing <c>orders/**</c> and <c>support/**</c> are served by
/// the same process.
/// </para>
/// <para>
/// <b>Every path here is rooted at <c>/docs/{slug}/</c>, and that is load-bearing.</b> The Gateway
/// proxies <c>docs/{slug}/**</c> straight through with an identity path transform, exactly like
/// <c>orders/**</c> — so the UI's own asset and document URLs resolve to the same paths whether the
/// browser arrived at the service directly on its container port or through the Gateway on
/// <c>:3000</c>. Serving the UI at a root path (<c>/scalar/v1</c>) and rewriting the prefix at the
/// edge would break exactly that: the HTML would come back referencing <c>/openapi/v1.json</c>,
/// which is a path the Gateway has no route for.
/// </para>
/// </summary>
/// <param name="Slug">
/// The service's path segment, lowercase — <c>orders</c>, <c>restaurants</c>. It matches the
/// public route prefix wherever there is one, so the documentation URL for a service is derivable
/// from the URL of any of its endpoints.
/// </param>
/// <param name="Title">The document title, shown in both UIs and in the browser tab.</param>
/// <param name="Description">
/// What the service owns, in a sentence or two. This is the one place a reader who opens the
/// documentation UI learns why this API exists separately from the other six.
/// </param>
public sealed record ApiDocumentationDescriptor(string Slug, string Title, string Description)
{
    /// <summary>
    /// The single document name. There is no versioning story here yet — one document per service,
    /// called <c>v1</c> — and the constant exists so the route pattern, the UI endpoint and the
    /// completeness test cannot disagree about it.
    /// </summary>
    public const string DocumentName = "v1";

    /// <summary>The root every documentation surface of this service hangs off.</summary>
    public string PathPrefix { get; } = $"/docs/{Slug}";

    /// <summary>
    /// The pattern <c>MapOpenApi</c> is mapped with. Keeps the <c>{documentName}</c> placeholder the
    /// framework substitutes, so adding a second document later needs no new route.
    /// </summary>
    public string OpenApiRoutePattern => $"{PathPrefix}/openapi/{{documentName}}.json";

    /// <summary>The concrete URL of the one document, for the two UIs to fetch.</summary>
    public string OpenApiDocumentPath => $"{PathPrefix}/openapi/{DocumentName}.json";

    /// <summary>The Scalar reference UI — the presentable one.</summary>
    public string ScalarPath => $"{PathPrefix}/scalar";

    /// <summary>
    /// The Swagger UI. Swashbuckle's middleware wants the prefix without a leading slash, hence the
    /// separate <see cref="SwaggerRoutePrefix"/>; this is the same surface as a browsable URL.
    /// </summary>
    public string SwaggerPath => $"{PathPrefix}/swagger";

    /// <summary><see cref="SwaggerPath"/> in the form <c>UseSwaggerUI</c>'s RoutePrefix takes.</summary>
    public string SwaggerRoutePrefix => SwaggerPath.TrimStart('/');

    /// <summary>
    /// True when <paramref name="path"/> is any of this service's documentation surfaces — the
    /// document, either UI, or an asset one of them loads.
    /// <para>
    /// Matched on whole segments rather than characters, for the same reason
    /// <see cref="Security.SecurityHeadersOptions.IsDocumentationPath"/> is: a prefix match on
    /// characters also matches a neighbouring route that merely starts with the same letters, and
    /// here that would be an authorization gate applied — or not applied — to the wrong path.
    /// </para>
    /// </summary>
    public bool Covers(string? path) =>
        !string.IsNullOrEmpty(path) &&
        (path.Equals(PathPrefix, StringComparison.OrdinalIgnoreCase) ||
         path.StartsWith(PathPrefix + "/", StringComparison.OrdinalIgnoreCase));
}

namespace FoodDeliveryService.Common.Presentation.Documentation;

/// <summary>
/// The deployment's say over the API documentation, from the <c>"ApiDocumentation"</c>
/// configuration section. The per-service <em>content</em> is code
/// (<see cref="ApiDocumentationDescriptor"/>); this is the part an environment gets to change.
/// </summary>
public sealed class ApiDocumentationOptions
{
    public const string SectionName = "ApiDocumentation";

    /// <summary>
    /// On everywhere by default, which is the change Milestone G makes: the documentation used to be
    /// mapped inside <c>if (app.Environment.IsDevelopment())</c>, so the documented surface was
    /// invisible from every environment anybody other than the author would look at.
    /// <para>
    /// It stays configurable because "no documentation surface at all" is a legitimate posture for a
    /// deployment that has decided its schema is not public — a stronger one than the authorization
    /// gate below, and the only one that also removes the endpoints.
    /// </para>
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Contact name rendered in the document's <c>info.contact</c>.</summary>
    public string ContactName { get; set; } = "FoodDeliveryService";

    /// <summary>Contact URL rendered in the document's <c>info.contact</c>.</summary>
    public string ContactUrl { get; set; } = "https://github.com/branimirteodorovic/FoodDeliveryService";

    /// <summary>
    /// The document version string. Distinct from <see cref="ApiDocumentationDescriptor.DocumentName"/>,
    /// which is the route segment: this is what the document claims about itself.
    /// </summary>
    public string Version { get; set; } = "1.0.0";
}

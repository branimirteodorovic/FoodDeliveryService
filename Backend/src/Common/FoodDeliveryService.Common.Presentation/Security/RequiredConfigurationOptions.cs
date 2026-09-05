namespace FoodDeliveryService.Common.Presentation.Security;

/// <summary>
/// The set of configuration keys a host declares it cannot start without — Feature 3.7 Milestone E.
/// <para>
/// Populated by <see cref="RequiredConfigurationExtensions.AddRequiredConfiguration"/> and validated
/// by <see cref="RequiredConfigurationValidator"/> at startup. The value of each key is captured at
/// registration time rather than read during validation, because the point of the check is the
/// configuration the host was *built* with.
/// </para>
/// </summary>
public sealed class RequiredConfigurationOptions
{
    /// <summary>
    /// False in Development, where the committed local values are deliberately weak or absent and a
    /// failed boot would only get in the way. Everywhere else a missing key is a startup failure.
    /// </summary>
    public bool Enforced { get; set; }

    /// <summary>
    /// The environment name, quoted back in the failure message — "this host is running as
    /// Kubernetes" is the piece of context that makes the message actionable.
    /// </summary>
    public string EnvironmentName { get; set; } = string.Empty;

    /// <summary>
    /// Configuration key (colon-separated, as written in <c>appsettings.json</c>) → the value the
    /// host resolved for it, or null. Sorted so the failure message lists keys in a stable order.
    /// </summary>
    public IDictionary<string, string?> Values { get; } =
        new SortedDictionary<string, string?>(StringComparer.Ordinal);
}

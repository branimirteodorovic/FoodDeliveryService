using Microsoft.Extensions.Configuration;

namespace FoodDeliveryService.Common.Presentation.Security;

/// <summary>
/// Reads a configured array as a <b>replacement</b> for an options default, rather than an addition
/// to it.
/// <para>
/// <see cref="ConfigurationBinder.Bind(IConfiguration, object)"/> <b>appends</b> to an array
/// property that already holds values — it reads the current contents and adds the configured ones
/// on top. For a property whose default is empty that is invisible; for
/// <c>SecurityHeadersOptions.DocumentationPathPrefixes</c> and
/// <c>EdgeCorsOptions.ExposedHeaders</c>, whose defaults are deliberately non-empty, it means a
/// deployment that narrows the list silently gets its list <i>plus</i> the four it was trying to
/// replace. Found by a test, not by reasoning: the binder's behaviour here is not what the property
/// shape suggests.
/// </para>
/// </summary>
internal static class ConfiguredArray
{
    /// <summary>
    /// The values under <paramref name="key"/> when the section defines it, otherwise
    /// <paramref name="fallback"/> unchanged.
    /// </summary>
    internal static string[] Replace(IConfiguration section, string key, string[] fallback)
    {
        IConfigurationSection child = section.GetSection(key);

        return child.Exists() ? child.Get<string[]>() ?? [] : fallback;
    }
}

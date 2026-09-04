using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

// Both namespaces above define an IPNetwork, and the ASP.NET Core one is deprecated in .NET 10 —
// `KnownIPNetworks` is typed on System.Net's. The alias resolves the ambiguity to the supported type
// so the next `using` added to this file cannot silently pick the obsolete one.
using IPNetwork = System.Net.IPNetwork;

namespace FoodDeliveryService.Common.Presentation.Security;

/// <summary>
/// Restores the real client's address and scheme at the edge — Feature 3.7 Milestone D §5.2.
/// <para>
/// This fixes a live defect rather than adding a precaution. The edge rate limiter partitions
/// anonymous callers by <c>HttpContext.Connection.RemoteIpAddress</c> (<c>RateLimitClient</c>).
/// Behind any TLS-terminating proxy — which is the intended deployment, since nothing in this
/// repository terminates TLS — that address is the *proxy*, so every anonymous request on the
/// platform shares one bucket and the per-client limit silently degrades into a global one. The same
/// substitution puts the proxy's address in every Serilog request log and every trace.
/// <c>X-Forwarded-Proto</c> matters too: without it a proxied HTTPS request looks like plain HTTP
/// here, so <see cref="SecurityHeadersMiddleware"/> never emits HSTS and any scheme-sensitive
/// redirect points at the wrong one.
/// </para>
/// <para>
/// <b>Gateway only.</b> Module hosts sit behind YARP on a private network, are unreachable from a
/// client (Hard Rule 10), and keep the framework default — a second hop of header rewriting there
/// would only widen the surface for no gain.
/// </para>
/// </summary>
public static class EdgeForwardedHeadersExtensions
{
    private const string LoggerCategory = "FoodDeliveryService.Security";

    /// <summary>
    /// Binds <see cref="EdgeForwardedHeadersOptions"/> and configures the framework's
    /// <see cref="ForwardedHeadersOptions"/> from it. Pair with <see cref="UseEdgeForwardedHeaders"/>.
    /// </summary>
    public static IServiceCollection AddEdgeForwardedHeaders(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var options = new EdgeForwardedHeadersOptions();

        configuration.GetSection(EdgeForwardedHeadersOptions.SectionName).Bind(options);

        services.AddSingleton(options);

        services.Configure<ForwardedHeadersOptions>(forwarded =>
        {
            // Host is deliberately absent. The Gateway generates no absolute URLs, so an inbound
            // X-Forwarded-Host would only ever be a way to poison a link the platform does not send.
            forwarded.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;

            forwarded.ForwardLimit = options.ForwardLimit;

            // The framework pre-trusts loopback (127.0.0.1/8, ::1). Cleared, so the trust list is
            // exactly what configuration says and nothing more: a sidecar or a compromised process
            // on the same host is not automatically a trusted proxy just because it is local.
            // KnownIPNetworks, not the obsolete KnownNetworks: ASP.NET Core's own IPNetwork type is
            // deprecated in favour of System.Net.IPNetwork, and the deprecation is an *error* here
            // (TreatWarningsAsErrors), so there is no quiet way onto the old list.
            forwarded.KnownProxies.Clear();
            forwarded.KnownIPNetworks.Clear();

            foreach (string proxy in options.KnownProxies)
            {
                forwarded.KnownProxies.Add(IPAddress.Parse(proxy));
            }

            foreach (string network in options.KnownNetworks)
            {
                forwarded.KnownIPNetworks.Add(ParseNetwork(network));
            }
        });

        return services;
    }

    /// <summary>
    /// Adds the middleware. Place it <b>first in the pipeline</b> — before
    /// <c>UseRequestCorrelation()</c>, <c>UseSerilogRequestLogging()</c> and the rate limiter —
    /// because every one of them reads the address or the scheme it rewrites.
    /// </summary>
    public static IApplicationBuilder UseEdgeForwardedHeaders(this IApplicationBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var options = app.ApplicationServices.GetService<EdgeForwardedHeadersOptions>()
            ?? throw new InvalidOperationException(
                $"{nameof(UseEdgeForwardedHeaders)}() requires " +
                $"{nameof(AddEdgeForwardedHeaders)}(configuration) on the host's services.");

        ILogger logger = app.ApplicationServices.GetRequiredService<ILoggerFactory>().CreateLogger(LoggerCategory);

        if (!options.Enabled)
        {
            logger.LogWarning(
                "Forwarded headers are DISABLED. Behind a proxy, every anonymous caller will share " +
                "one rate-limit partition and the logs will record the proxy's address as the client's.");

            return app;
        }

        if (options.HasTrustedUpstream)
        {
            logger.LogInformation(
                "Forwarded headers are on, trusting {ProxyCount} proxy address(es) and " +
                "{NetworkCount} network(s), forward limit {ForwardLimit}.",
                options.KnownProxies.Length,
                options.KnownNetworks.Length,
                options.ForwardLimit);
        }
        else
        {
            // Deliberately a warning, and only in this branch: running behind a proxy with an empty
            // trust list is the state the milestone set out to fix, and it is invisible otherwise —
            // everything works, the limiter is just no longer per client.
            logger.LogWarning(
                "Forwarded headers are on but nothing is trusted: X-Forwarded-For and " +
                "X-Forwarded-Proto will be ignored. That is correct when the Gateway is exposed " +
                "directly. Behind a proxy, set '{Section}:{Key}' to the proxy's network (e.g. the " +
                "pod CIDR), or the edge rate limiter partitions every anonymous caller into one bucket.",
                EdgeForwardedHeadersOptions.SectionName,
                nameof(EdgeForwardedHeadersOptions.KnownNetworks));
        }

        return app.UseForwardedHeaders();
    }

    /// <summary>
    /// Parses <c>address/prefixLength</c> — the shape a CIDR is written in everywhere else (a
    /// Kubernetes pod CIDR, an Azure subnet) — and fails with the offending value named, because
    /// <see cref="IPNetwork.Parse(string)"/>'s own message does not say which configuration key it
    /// came from.
    /// </summary>
    private static IPNetwork ParseNetwork(string value)
    {
        if (!IPNetwork.TryParse(value, out IPNetwork network))
        {
            throw new InvalidOperationException(
                $"'{EdgeForwardedHeadersOptions.SectionName}:" +
                $"{nameof(EdgeForwardedHeadersOptions.KnownNetworks)}' entry '{value}' is not CIDR " +
                "notation. Expected something like '10.244.0.0/16'.");
        }

        return network;
    }
}

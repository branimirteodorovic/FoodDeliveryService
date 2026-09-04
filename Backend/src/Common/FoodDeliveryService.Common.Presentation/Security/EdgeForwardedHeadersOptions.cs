namespace FoodDeliveryService.Common.Presentation.Security;

/// <summary>
/// Which upstream proxies the Gateway believes, from the <c>"ForwardedHeaders"</c> configuration
/// section.
/// <para>
/// <b>Nothing extra is trusted by default.</b> <c>X-Forwarded-For</c> is a client-supplied header:
/// honouring it from an arbitrary sender lets any caller pick its own rate-limit partition key and
/// its own address in the logs, which is a worse bug than the one forwarded headers exist to fix.
/// So the trust list is configuration, it starts empty, and the framework's loopback defaults are
/// the only thing believed until a deployment says otherwise.
/// </para>
/// </summary>
public sealed class EdgeForwardedHeadersOptions
{
    public const string SectionName = "ForwardedHeaders";

    /// <summary>
    /// On by default. With an empty trust list this changes nothing observable — the middleware runs
    /// and discards every untrusted header — so the switch exists to take the middleware out of the
    /// pipeline entirely, not to make it safe.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Individual proxy addresses to trust, e.g. an ingress controller's service IP.
    /// </summary>
    public string[] KnownProxies { get; set; } = [];

    /// <summary>
    /// CIDR networks to trust, e.g. <c>10.244.0.0/16</c> for a cluster's pod network. This is the
    /// one a Kubernetes or Azure deployment actually needs: the proxy's address is not stable, its
    /// network is.
    /// </summary>
    public string[] KnownNetworks { get; set; } = [];

    /// <summary>
    /// How many entries to walk back from the right of <c>X-Forwarded-For</c>. One means "the
    /// immediate proxy", which is right for a single TLS terminator. Raise it only alongside adding
    /// every intermediate hop to the trust list — a limit larger than the number of trusted proxies
    /// is how a spoofed left-hand entry becomes the client address.
    /// </summary>
    public int ForwardLimit { get; set; } = 1;

    /// <summary>True when a deployment has actually named something to trust.</summary>
    public bool HasTrustedUpstream => KnownProxies.Length > 0 || KnownNetworks.Length > 0;
}

using System.Net;
using Microsoft.AspNetCore.HttpOverrides;

namespace ProjectPrime.Server.Node;

/// <summary>Only explicitly trusted ingress may rewrite the source address or
/// scheme used by Node security middleware. Forwarding is applied before the
/// WebSocket upgrade and all rate-limit policies.</summary>
public sealed class NodeNetworkOptions
{
    public List<string> TrustedProxies { get; set; } = [];
    public List<string> TrustedNetworks { get; set; } = [];

    public static void ConfigureForwarding(ForwardedHeadersOptions forwarding, NodeNetworkOptions settings)
    {
        forwarding.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
        forwarding.ForwardLimit = 1;
        forwarding.RequireHeaderSymmetry = true;
        forwarding.KnownProxies.Clear();
        forwarding.KnownIPNetworks.Clear();
        foreach (string configured in settings.TrustedProxies)
        {
            if (!IPAddress.TryParse(configured, out IPAddress? address)
                || address.ToString() != configured)
                throw new InvalidOperationException("Node:Network:TrustedProxies entries must be canonical IP literals.");
            forwarding.KnownProxies.Add(address);
        }
        foreach (string configured in settings.TrustedNetworks)
        {
            string[] parts = configured.Split('/', StringSplitOptions.None);
            if (parts.Length != 2 || !IPAddress.TryParse(parts[0], out IPAddress? address)
                || address.ToString() != parts[0]
                || !int.TryParse(parts[1], out int prefix)
                || prefix < 0 || prefix > (address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork ? 32 : 128))
                throw new InvalidOperationException("Node:Network:TrustedNetworks entries must be canonical CIDR networks.");
            forwarding.KnownIPNetworks.Add(new System.Net.IPNetwork(address, prefix));
        }
    }

    public static string EffectiveRemoteAddress(HttpContext context)
    {
        IPAddress? address = context.Connection.RemoteIpAddress;
        if (address?.IsIPv4MappedToIPv6 == true) address = address.MapToIPv4();
        return address?.ToString().ToLowerInvariant() ?? "unknown";
    }

    public static void Validate(NodeNetworkOptions settings)
        => ConfigureForwarding(new ForwardedHeadersOptions(), settings);
}

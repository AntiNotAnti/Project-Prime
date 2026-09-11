using System.Net;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using MphRead.Backend.Identity;
using MphRead.Backend.Tickets;

namespace MphRead.Backend;

public sealed class BackendSecurityOptions
{
    public string? PublicUrl { get; set; }
    public bool AllowLoopbackHttp { get; set; }
    /// <summary>Temporary Development-only HTTP access for the Project Prime test Backend.</summary>
    public bool AllowRemoteHttp { get; set; }
    public int MaxConcurrentRequests { get; set; } = 128;
    public List<string> TrustedProxies { get; set; } = [];
    public List<string> TrustedNetworks { get; set; } = [];
}

public static class BackendSecurity
{
    public const string TemporaryDevelopmentBackendHost = "51.161.113.128";
    private static readonly HashSet<string> DevelopmentCredentialHashes = new(StringComparer.OrdinalIgnoreCase)
    {
        Hash("change-me"),
        Hash("changeme"),
        Hash("development"),
        Hash("password"),
        Hash("test-only-server-credential-not-for-production-123456"),
        new('0', 64)
    };

    public static string EndpointPartitionKey(HttpContext http)
    {
        string route = http.GetEndpoint() is RouteEndpoint endpoint
            ? endpoint.RoutePattern.RawText ?? http.Request.Path.Value ?? "/"
            : http.Request.Path.Value ?? "/";
        string? subject = http.User.Identity?.IsAuthenticated == true
            ? http.User.FindFirstValue(ClaimTypes.NameIdentifier)
            : null;
        string caller = Guid.TryParse(subject, out Guid playerId) && playerId != Guid.Empty
            ? $"player:{playerId:D}"
            : $"ip:{CanonicalAddress(http.Connection.RemoteIpAddress)}";
        return $"{http.Request.Method}:{route}|{caller}";
    }

    public static string IpPartitionKey(HttpContext http)
        => $"ip:{CanonicalAddress(http.Connection.RemoteIpAddress)}";

    /// <summary>Pre-auth machine traffic is partitioned only by the effective
    /// source address. X-Server-Id is an untrusted credential selector until
    /// <see cref="GameServerRegistry"/> authenticates it in the endpoint, so a
    /// caller cannot mint a fresh limiter bucket by changing that header.</summary>
    public static string MachinePartitionKey(HttpContext http)
        => $"machine-ip:{CanonicalAddress(http.Connection.RemoteIpAddress)}";

    public static void ConfigureForwarding(ForwardedHeadersOptions forwarding, BackendSecurityOptions settings)
    {
        forwarding.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
        forwarding.ForwardLimit = 1;
        forwarding.RequireHeaderSymmetry = true;
        forwarding.KnownIPNetworks.Clear();
        forwarding.KnownProxies.Clear();
        foreach (string configured in settings.TrustedProxies)
        {
            if (!IPAddress.TryParse(configured, out IPAddress? address)
                || !string.Equals(address.ToString(), configured, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Backend__TrustedProxies entries must be canonical IP literals.");
            }
            forwarding.KnownProxies.Add(address);
        }
        foreach (string configured in settings.TrustedNetworks)
        {
            if (!TryParseNetwork(configured, out IPAddress? address, out int prefixLength))
                throw new InvalidOperationException("Backend__TrustedNetworks entries must be canonical CIDR networks.");
            forwarding.KnownIPNetworks.Add(new System.Net.IPNetwork(address!, prefixLength));
        }
    }

    public static void ValidateCommon(BackendSecurityOptions settings)
    {
        if (settings.MaxConcurrentRequests is < 1 or > 10_000)
            throw new InvalidOperationException("Backend__MaxConcurrentRequests must be between 1 and 10000.");
        if (settings.AllowRemoteHttp)
            throw new InvalidOperationException("Backend__AllowRemoteHttp is retired; use HTTPS or loopback HTTP in Development only.");
        // Parse and canonicalize every entry even when forwarding is not otherwise used.
        ConfigureForwarding(new ForwardedHeadersOptions(), settings);
    }

    public static bool IsExplicitLoopbackDevelopmentRequest(HttpContext http, BackendSecurityOptions settings)
        => settings.AllowLoopbackHttp
            && http.Connection.LocalIpAddress is { } local && IPAddress.IsLoopback(local)
            && http.Connection.RemoteIpAddress is { } remote && IPAddress.IsLoopback(remote);

    public static bool IsExplicitRemoteHttpDevelopmentRequest(HttpContext http, BackendSecurityOptions settings)
        => false;

    public static void ValidateProductionListeners(IConfiguration configuration, BackendSecurityOptions security)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(security);

        string[] listeners = ConfiguredListeners(configuration);
        bool trustedTlsTermination = security.TrustedProxies.Count > 0
            || security.TrustedNetworks.Count > 0;
        foreach (string listener in listeners)
        {
            if (listener.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) continue;
            if (trustedTlsTermination
                && listener.StartsWith("http://", StringComparison.OrdinalIgnoreCase)) continue;
            throw new InvalidOperationException(
                "Production Backend listeners must use HTTPS. Plain HTTP requires an explicitly configured trusted TLS terminator.");
        }
    }

    public static void ValidateProduction(BackendSecurityOptions security, AccountOptions accounts,
        TicketOptions tickets, GameServerOptions servers, bool emailConfigured,
        bool ticketsConfigured)
    {
        if (!Uri.TryCreate(security.PublicUrl, UriKind.Absolute, out Uri? publicUrl)
            || publicUrl.Scheme != Uri.UriSchemeHttps || !string.IsNullOrEmpty(publicUrl.UserInfo)
            || !string.IsNullOrEmpty(publicUrl.Query) || !string.IsNullOrEmpty(publicUrl.Fragment))
            throw new InvalidOperationException("Backend__PublicUrl must be an HTTPS public origin in production.");
        if (string.IsNullOrWhiteSpace(accounts.DataProtectionKeyPath)
            || !Path.IsPathFullyQualified(accounts.DataProtectionKeyPath))
            throw new InvalidOperationException("Accounts__DataProtectionKeyPath must be an absolute durable path in production.");
        if (!accounts.RequireConfirmedEmail)
            throw new InvalidOperationException("Accounts__RequireConfirmedEmail must be true in production.");
        if (!emailConfigured)
            throw new InvalidOperationException("A complete Email SMTP provider configuration is required in production.");
        if (!ticketsConfigured)
            throw new InvalidOperationException("Ticket signing credentials are required in production.");
        if (!Uri.TryCreate(tickets.Issuer, UriKind.Absolute, out Uri? issuer)
            || !Uri.Compare(publicUrl, issuer, UriComponents.SchemeAndServer, UriFormat.Unescaped,
                StringComparison.OrdinalIgnoreCase).Equals(0))
            throw new InvalidOperationException("Tickets__Issuer must use the configured production public origin.");

        var credentialHashes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (GameServerRegistration server in servers.Servers.Where(server => server.Enabled))
        {
            if (DevelopmentCredentialHashes.Contains(server.ApiKeySha256)
                || !credentialHashes.Add(server.ApiKeySha256))
                throw new InvalidOperationException("Enabled game servers require distinct non-default credentials in production.");
        }
    }

    private static string CanonicalAddress(IPAddress? address)
    {
        if (address == null) return "unknown";
        if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();
        return address.ToString().ToLowerInvariant();
    }

    private static string Hash(string value)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private static bool TryParseNetwork(string value, out IPAddress? address, out int prefixLength)
    {
        address = null; prefixLength = 0;
        string[] parts = value.Split('/', StringSplitOptions.None);
        if (parts.Length != 2 || !IPAddress.TryParse(parts[0], out IPAddress? parsed)
            || !int.TryParse(parts[1], out int prefix)
            || parsed.ToString() != parts[0]) return false;
        int maximum = parsed.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork ? 32 : 128;
        if (prefix is < 0 || prefix > maximum) return false;
        address = parsed; prefixLength = prefix; return true;
    }

    private static string[] ConfiguredListeners(IConfiguration configuration)
    {
        IConfigurationSection endpoints = configuration.GetSection("Kestrel:Endpoints");
        IConfigurationSection[] configuredEndpoints = endpoints.GetChildren().ToArray();
        if (configuredEndpoints.Length > 0)
        {
            string[] values = configuredEndpoints.Select(endpoint => endpoint["Url"]?.Trim() ?? "")
                .ToArray();
            if (values.Any(string.IsNullOrWhiteSpace))
                throw new InvalidOperationException("Every production Kestrel endpoint must define an explicit URL.");
            return values;
        }

        string? urls = configuration["urls"];
        if (!string.IsNullOrWhiteSpace(urls))
        {
            string[] values = urls.Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            if (values.Length == 0)
                throw new InvalidOperationException("Production Backend listener configuration is empty.");
            return values;
        }

        string[] httpPorts = Ports(configuration["HTTP_PORTS"]);
        string[] httpsPorts = Ports(configuration["HTTPS_PORTS"]);
        if (httpPorts.Length > 0 || httpsPorts.Length > 0)
        {
            return httpPorts.Select(port => $"http://*:{port}")
                .Concat(httpsPorts.Select(port => $"https://*:{port}"))
                .ToArray();
        }

        // Kestrel's unconfigured fallback is an HTTP localhost listener.
        return ["http://localhost:5000"];
    }

    private static string[] Ports(string? value) => string.IsNullOrWhiteSpace(value)
        ? []
        : value.Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
}

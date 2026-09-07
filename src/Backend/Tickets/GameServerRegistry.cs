using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using MphRead.Identity;

namespace MphRead.Backend.Tickets;

public sealed class GameServerOptions
{
    public List<GameServerRegistration> Servers { get; set; } = [];
}

public sealed class GameServerRegistration
{
    public Guid Id { get; set; }
    public bool Enabled { get; set; }
    public string ApiKeySha256 { get; set; } = "";
    public string? PublicAddress { get; set; }
    public int PublicPort { get; set; }
    public MatchTrustClass TrustClass { get; set; } = MatchTrustClass.Community;
}

public sealed record RankedAvailabilityStatus(bool Available, string? UnavailableReason);

public sealed class GameServerRegistry
{
    public const string RankedUnavailableReason =
        "Ranked is disabled for public deployments until authenticated transport proof-of-possession is implemented.";
    private readonly Dictionary<Guid, byte[]> _credentials = [];
    private readonly ConcurrentDictionary<Guid, Guid> _sessions = new();
    private readonly Dictionary<Guid, MatchTrustClass> _trust = [];
    private readonly Dictionary<Guid, IPEndPoint> _endpoints = [];
    public RankedAvailabilityStatus RankedAvailability { get; }

    public GameServerRegistry(IOptions<GameServerOptions> options, IHostEnvironment environment)
    {
        bool publicDeployment = !environment.IsDevelopment() && !environment.IsEnvironment("Testing");
        RankedAvailability = publicDeployment
            ? new(false, RankedUnavailableReason)
            : new(true, null);
        foreach (var server in options.Value.Servers)
        {
            if (!server.Enabled) { continue; }
            if (server.Id == Guid.Empty || !Enum.IsDefined(server.TrustClass) || server.ApiKeySha256.Length != 64
                || !server.ApiKeySha256.All(char.IsAsciiHexDigit))
                throw new InvalidOperationException("An enabled game server requires a unique ID and SHA256 credential hash.");
            if (!_credentials.TryAdd(server.Id, Convert.FromHexString(server.ApiKeySha256)))
                throw new InvalidOperationException("Game server IDs must be unique.");
            _trust.Add(server.Id, server.TrustClass);
            if (server.PublicAddress != null || server.PublicPort != 0)
            {
                if (!ValidPublicEndpoint(server.PublicAddress, server.PublicPort))
                    throw new InvalidOperationException("A game ticket destination requires a canonical unicast IPv4 address and UDP port 1..65535.");
                _endpoints.Add(server.Id, new IPEndPoint(IPAddress.Parse(server.PublicAddress!), server.PublicPort));
            }
        }
    }

    public bool TryRegister(Guid serverId, string secret, Guid incarnation)
    {
        if (incarnation == Guid.Empty || !TryAuthenticate(serverId, secret, out _)) return false;
        _sessions[serverId] = incarnation;
        return true;
    }

    public bool TryAuthenticate(Guid serverId, string secret, out MatchTrustClass trust)
    {
        trust = default;
        if (secret.Length is < 32 or > 512 || !_credentials.TryGetValue(serverId, out var expected)) return false;
        if (!CryptographicOperations.FixedTimeEquals(SHA256.HashData(Encoding.UTF8.GetBytes(secret)), expected)) return false;
        MatchTrustClass authenticatedTrust = _trust[serverId];
        if (authenticatedTrust == MatchTrustClass.Ranked && !RankedAvailability.Available) return false;
        trust = authenticatedTrust;
        return true;
    }

    public static bool ValidPublicEndpoint(string? address, int port)
        => port is >= 1 and <= 65535 && IPAddress.TryParse(address, out var ip)
            && ip.AddressFamily == AddressFamily.InterNetwork && ip.ToString() == address
            && ip.GetAddressBytes()[0] is > 0 and < 224 && !ip.Equals(IPAddress.Broadcast);

    public bool TryGetTicketDestination(Guid serverId, out Guid incarnation, out string address, out int port)
    {
        incarnation = Guid.Empty; address = ""; port = 0;
        if (_trust.GetValueOrDefault(serverId) == MatchTrustClass.Ranked && !RankedAvailability.Available) return false;
        if (!_sessions.TryGetValue(serverId, out incarnation) || !_endpoints.TryGetValue(serverId, out var endpoint)) return false;
        address = endpoint.Address.ToString(); port = endpoint.Port; return true;
    }

    public bool TryGetIncarnation(Guid serverId, out Guid incarnation)
    {
        incarnation = Guid.Empty;
        return !(_trust.GetValueOrDefault(serverId) == MatchTrustClass.Ranked && !RankedAvailability.Available)
            && _sessions.TryGetValue(serverId, out incarnation);
    }
}

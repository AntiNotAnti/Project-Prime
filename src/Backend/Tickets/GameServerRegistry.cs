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
    public MatchTrustClass TrustClass { get; set; } = MatchTrustClass.Community;
}

public sealed record RankedAvailabilityStatus(bool Available, string? UnavailableReason);

public sealed class GameServerRegistry
{
    public const string RankedUnavailableReason =
        "Ranked is disabled for public deployments until authenticated transport proof-of-possession is implemented.";
    private readonly Dictionary<Guid, byte[]> _credentials = [];
    private readonly Dictionary<Guid, MatchTrustClass> _trust = [];
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
        }
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

}

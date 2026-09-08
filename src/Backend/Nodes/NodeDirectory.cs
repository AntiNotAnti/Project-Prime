using MphRead.Backend.Tickets;
using MphRead.Identity;

namespace MphRead.Backend.Nodes;

public sealed record NodeRegistration(Guid Incarnation, string Name, string Region, string PublicControlUri,
    int ProtocolVersion, string BuildVersion, string ContentHash, int Capacity);
public sealed record NodeHeartbeat(Guid Incarnation, int OnlineUsers, int LobbyCount, int ActiveMatches);
public sealed record NodeListing(Guid NodeId, string Name, string Region, string PublicControlUri,
    int ProtocolVersion, string BuildVersion, string ContentHash, int Capacity, int OnlineUsers,
    int LobbyCount, int ActiveMatches, string TrustClass, DateTimeOffset LastHeartbeat);

/// <summary>Ephemeral discovery only. Restart clears listings; Nodes republish. No live
/// match or admission authority depends on retention of this directory.</summary>
public sealed class NodeDirectory(GameServerRegistry owners, TimeProvider clock)
{
    public static readonly TimeSpan OnlineLifetime = TimeSpan.FromSeconds(90);
    private readonly object _gate = new();
    private readonly Dictionary<Guid, (Guid Incarnation, NodeListing Listing)> _nodes = [];

    public bool Authenticate(Guid nodeId, string secret, out MatchTrustClass trust)
        => owners.TryAuthenticate(nodeId, secret, out trust);

    public bool Register(Guid nodeId, string secret, NodeRegistration value)
    {
        if (!Authenticate(nodeId, secret, out var trust)) return false;
        Validate(value);
        var listing = new NodeListing(nodeId, value.Name, value.Region, value.PublicControlUri,
            value.ProtocolVersion, value.BuildVersion, value.ContentHash, value.Capacity, 0, 0, 0,
            trust is MatchTrustClass.VerifiedCasual or MatchTrustClass.Ranked or MatchTrustClass.Tournament ? "verified" : "community", clock.GetUtcNow());
        lock (_gate) _nodes[nodeId] = (value.Incarnation, listing);
        return true;
    }

    public bool Heartbeat(Guid nodeId, string secret, NodeHeartbeat value)
    {
        if (!Authenticate(nodeId, secret, out _)) return false;
        lock (_gate)
        {
            if (!_nodes.TryGetValue(nodeId, out var current) || current.Incarnation != value.Incarnation) return false;
            if (value.OnlineUsers < 0 || value.OnlineUsers > current.Listing.Capacity
                || value.LobbyCount is < 0 or > 10000 || value.ActiveMatches is < 0 or > 10000)
                throw new ArgumentException("Invalid Node population.");
            _nodes[nodeId] = (current.Incarnation, current.Listing with { OnlineUsers = value.OnlineUsers,
                LobbyCount = value.LobbyCount, ActiveMatches = value.ActiveMatches, LastHeartbeat = clock.GetUtcNow() });
            return true;
        }
    }

    public NodeListing[] Browse(int protocol, string build, string content)
    {
        lock (_gate) return _nodes.Values.Select(x => x.Listing)
            .Where(x => Online(x) && x.ProtocolVersion == protocol && x.BuildVersion == build && x.ContentHash == content)
            .OrderBy(x => x.NodeId).ToArray();
    }

    public NodeListing? FindOnline(Guid nodeId)
    {
        lock (_gate) return _nodes.TryGetValue(nodeId, out var value) && Online(value.Listing) ? value.Listing : null;
    }

    private bool Online(NodeListing value) => clock.GetUtcNow() - value.LastHeartbeat < OnlineLifetime;
    private static bool Text(string? value, int max) => value is { Length: > 0 } && value.Length <= max
        && value.All(c => char.IsAscii(c) && !char.IsControl(c));
    private static void Validate(NodeRegistration value)
    {
        if (value.Incarnation == Guid.Empty || !Text(value.Name, 64) || !Text(value.Region, 32)
            || !Text(value.BuildVersion, 64) || value.ProtocolVersion is < 1 or > 65535
            || value.ContentHash is not { Length: 64 } || !value.ContentHash.All(char.IsAsciiHexDigit)
            || value.Capacity is < 1 or > 10000 || value.PublicControlUri is not { Length: <= 256 }
            || !Uri.TryCreate(value.PublicControlUri, UriKind.Absolute, out var uri)
            || uri.Scheme != "wss" || uri.UserInfo.Length != 0 || uri.Query.Length != 0 || uri.Fragment.Length != 0)
            throw new ArgumentException("Invalid Node registration.");
    }
}

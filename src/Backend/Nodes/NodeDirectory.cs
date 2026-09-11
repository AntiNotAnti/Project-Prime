using System.Collections.Immutable;
using MphRead.Backend.Tickets;
using MphRead.Identity;
using ProjectPrime.Server.Shared;

namespace MphRead.Backend.Nodes;

public sealed record NodeRegistration(Guid Incarnation, string Name, string Region, string PublicControlUri,
    int ProtocolVersion, string BuildVersion, string ContentHash, int Capacity,
    long MapCatalogRevision = 0, int MapCount = 0, string? MapCatalogHash = null);
public sealed record NodeHeartbeat(Guid Incarnation, int OnlineUsers, int LobbyCount, int ActiveMatches);
public sealed record NodeListing(Guid NodeId, string Name, string Region, string PublicControlUri,
    int ProtocolVersion, string BuildVersion, string ContentHash, int Capacity, int OnlineUsers,
    int LobbyCount, int ActiveMatches, string TrustClass, DateTimeOffset LastHeartbeat,
    long MapCatalogRevision = 0, int MapCount = 0, string? MapCatalogHash = null);

public sealed class NodeDirectoryPageException(string code, string message, int statusCode)
    : Exception(message)
{
    public string Code { get; } = code;
    public int StatusCode { get; } = statusCode;
}

/// <summary>Ephemeral discovery only. Restart clears listings; Nodes republish. No live
/// match or admission authority depends on retention of this directory.</summary>
public sealed class NodeDirectory(GameServerRegistry owners, TimeProvider clock)
{
    public static readonly TimeSpan OnlineLifetime = TimeSpan.FromSeconds(90);
    private readonly object _gate = new();
    private readonly Dictionary<Guid, (Guid Incarnation, NodeListing Listing)> _nodes = [];
    private long _revision = 1;

    public bool Authenticate(Guid nodeId, string secret, out MatchTrustClass trust)
        => owners.TryAuthenticate(nodeId, secret, out trust);

    public bool Register(Guid nodeId, string secret, NodeRegistration value)
    {
        if (!Authenticate(nodeId, secret, out var trust)) return false;
        Validate(value);
        var listing = new NodeListing(nodeId, value.Name, value.Region, value.PublicControlUri,
            value.ProtocolVersion, value.BuildVersion, value.ContentHash, value.Capacity, 0, 0, 0,
            trust is MatchTrustClass.VerifiedCasual or MatchTrustClass.Ranked or MatchTrustClass.Tournament ? "verified" : "community",
            clock.GetUtcNow(), value.MapCatalogRevision, value.MapCount,
            value.MapCatalogHash?.ToLowerInvariant());
        lock (_gate)
        {
            PruneExpiredLocked();
            _nodes[nodeId] = (value.Incarnation, listing);
            _revision++;
        }
        return true;
    }

    public bool Heartbeat(Guid nodeId, string secret, NodeHeartbeat value)
    {
        if (!Authenticate(nodeId, secret, out _)) return false;
        lock (_gate)
        {
            PruneExpiredLocked();
            if (!_nodes.TryGetValue(nodeId, out var current) || current.Incarnation != value.Incarnation) return false;
            if (value.OnlineUsers < 0 || value.OnlineUsers > current.Listing.Capacity
                || value.LobbyCount is < 0 or > 10000 || value.ActiveMatches is < 0 or > 10000)
                throw new ArgumentException("Invalid Node population.");
            _nodes[nodeId] = (current.Incarnation, current.Listing with { OnlineUsers = value.OnlineUsers,
                LobbyCount = value.LobbyCount, ActiveMatches = value.ActiveMatches, LastHeartbeat = clock.GetUtcNow() });
            _revision++;
            return true;
        }
    }

    /// <summary>Removes only the currently advertised incarnation. A stale or
    /// replayed shutdown hint cannot remove a replacement Node.</summary>
    public bool Deregister(Guid nodeId, string secret, Guid incarnation)
    {
        if (!Authenticate(nodeId, secret, out _)) return false;
        if (incarnation == Guid.Empty) throw new ArgumentException("Invalid Node incarnation.");
        lock (_gate)
        {
            if (!_nodes.TryGetValue(nodeId, out var current) || current.Incarnation != incarnation) return true;
            _nodes.Remove(nodeId);
            _revision++;
            return true;
        }
    }

    public NodeListing[] Browse(int protocol, string build, string content)
    {
        lock (_gate)
        {
            PruneExpiredLocked();
            return _nodes.Values.Select(x => Snapshot(x.Listing))
                .Where(x => x.ProtocolVersion == protocol && x.BuildVersion == build
                    && x.ContentHash == content)
                .OrderBy(x => x.NodeId).ToArray();
        }
    }

    public NodeDirectoryPage BrowsePage(int protocol, string build, string content,
        int page = 0, long? revision = null)
    {
        if (page < 0 || page >= NodeDirectoryContract.MaximumPages)
            throw new NodeDirectoryPageException("invalid_page", "The Node directory page is invalid.",
                StatusCodes.Status400BadRequest);

        lock (_gate)
        {
            PruneExpiredLocked();
            if (revision is { } requested && requested != _revision)
                throw new NodeDirectoryPageException("directory_revision_changed",
                    "The Node directory changed while it was being read.",
                    StatusCodes.Status409Conflict);

            NodeListing[] matching = _nodes.Values.Select(x => Snapshot(x.Listing))
                .Where(x => x.ProtocolVersion == protocol
                    && x.BuildVersion == build && x.ContentHash == content)
                .OrderBy(x => x.NodeId).ToArray();
            if (matching.Length > NodeDirectoryContract.MaximumEntries)
                throw new NodeDirectoryPageException("directory_too_large",
                    "The Node directory is too large for the public client contract.",
                    StatusCodes.Status413PayloadTooLarge);

            int pageCount = Math.Max(1, (matching.Length + NodeDirectoryContract.MaximumPageEntries - 1)
                / NodeDirectoryContract.MaximumPageEntries);
            if (page >= pageCount)
                throw new NodeDirectoryPageException("invalid_page", "The Node directory page is invalid.",
                    StatusCodes.Status400BadRequest);

            NodeDirectoryEntry[] entries = matching
                .Skip(page * NodeDirectoryContract.MaximumPageEntries)
                .Take(NodeDirectoryContract.MaximumPageEntries)
                .Select(ToDirectoryEntry)
                .ToArray();
            return new(_revision, page, pageCount, matching.Length, entries.ToImmutableArray());
        }
    }

    public NodeListing? FindOnline(Guid nodeId)
    {
        lock (_gate)
        {
            PruneExpiredLocked();
            return _nodes.TryGetValue(nodeId, out var value) ? Snapshot(value.Listing) : null;
        }
    }

    private bool Online(NodeListing value) => clock.GetUtcNow() - value.LastHeartbeat < OnlineLifetime;

    private void PruneExpiredLocked()
    {
        Guid[] expired = _nodes
            .Where(pair => !Online(pair.Value.Listing))
            .Select(pair => pair.Key)
            .ToArray();
        if (expired.Length == 0) return;
        foreach (Guid nodeId in expired) _nodes.Remove(nodeId);
        _revision++;
    }
    private static bool Text(string? value, int max) => value is { Length: > 0 } && value.Length <= max
        && value.All(c => char.IsAscii(c) && !char.IsControl(c));
    private static void Validate(NodeRegistration value)
    {
        if (value.Incarnation == Guid.Empty || !Text(value.Name, 64) || !Text(value.Region, 32)
            || !Text(value.BuildVersion, 64) || value.ProtocolVersion is < 1 or > 65535
            || value.ContentHash is not { Length: 64 } || !value.ContentHash.All(char.IsAsciiHexDigit)
            || value.Capacity is < 1 or > 10000 || value.PublicControlUri is not { Length: <= 256 }
            || !NodeEndpointContract.TryValidatePublicControlUri(value.PublicControlUri, out _)
            || value.MapCatalogRevision < 0 || value.MapCount is < 0 or > 256
            || value.MapCatalogRevision == 0 && value.MapCount != 0
            || value.MapCatalogHash is { Length: > 0 } hash
                && (hash.Length != 64 || !hash.All(char.IsAsciiHexDigit)))
            throw new ArgumentException("Invalid Node registration.");
    }

    private static NodeListing Snapshot(NodeListing listing) => listing;

    private static NodeDirectoryEntry ToDirectoryEntry(NodeListing listing)
        => new(listing.NodeId, listing.Name, listing.Region, listing.PublicControlUri,
            listing.ProtocolVersion, listing.BuildVersion, listing.ContentHash, listing.Capacity,
            listing.OnlineUsers, listing.LobbyCount, listing.ActiveMatches, listing.TrustClass,
            listing.LastHeartbeat, listing.MapCatalogRevision, listing.MapCount,
            listing.MapCatalogHash);

}

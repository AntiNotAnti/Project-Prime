using System.Collections.Immutable;

namespace ProjectPrime.Server.Shared;

/// <summary>Wire limits for the anonymous Backend Node directory. A page is
/// deliberately much smaller than both the HTTP and control-frame limits so a
/// client can validate and assemble a bounded snapshot without buffering an
/// unbounded public response.</summary>
public static class NodeDirectoryContract
{
    public const int MaximumPageEntries = 32;
    public const int MaximumPages = 64;
    public const int MaximumEntries = MaximumPageEntries * MaximumPages;
}

/// <summary>Data-only public directory projection. Map identities are
/// advertised as bounded revision/count/hash metadata; the actual catalog is
/// delivered by the authenticated Node control connection.</summary>
public sealed record NodeDirectoryEntry(Guid NodeId, string Name, string Region,
    string PublicControlUri, int ProtocolVersion, string BuildVersion, string ContentHash,
    int Capacity, int OnlineUsers, int LobbyCount, int ActiveMatches, string TrustClass,
    DateTimeOffset LastHeartbeat, long MapCatalogRevision = 0, int MapCount = 0,
    string? MapCatalogHash = null);

/// <summary>One immutable, revision-pinned page of the public Node directory.
/// The client must reject mixed revisions and replace its prior directory only
/// after all pages for this snapshot are complete.</summary>
public sealed record NodeDirectoryPage(long Revision, int Page, int PageCount,
    int TotalEntries, ImmutableArray<NodeDirectoryEntry> Entries);

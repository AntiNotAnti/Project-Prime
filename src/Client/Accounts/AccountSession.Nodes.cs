using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using ProjectPrime.Server.Shared;

namespace MphRead.Mods.Accounts;

public sealed record NodeListing(Guid NodeId, string Name, string Region, string PublicControlUri, int ProtocolVersion,
    string BuildVersion, string ContentHash, int Capacity, int OnlineUsers, int LobbyCount, int ActiveMatches,
    string TrustClass, DateTimeOffset LastHeartbeat,
    long MapCatalogRevision = 0, int MapCount = 0, string? MapCatalogHash = null);
public sealed record NodeAdmissionTicket(string Ticket, DateTimeOffset ExpiresAt, Guid NodeId, string PublicControlUri)
{
    public override string ToString() => $"Node admission for {NodeId}";
}
public sealed partial class AccountSession
{
    public async Task<NodeListing[]> GetNodesAsync(int protocol, string build, string content, CancellationToken cancel = default)
    {
        // A directory revision can change between pages. Restart only the
        // complete read on the explicit conflict signal; never expose a
        // partially assembled snapshot to the launcher.
        for (int attempt = 0; attempt < 3; attempt++)
        {
            try { return await ReadNodesSnapshotAsync(protocol, build, content, cancel).ConfigureAwait(false); }
            catch (AccountServiceException error) when (
                error.StatusCode == System.Net.HttpStatusCode.Conflict
                && StringComparer.Ordinal.Equals(error.ErrorCode, "directory_revision_changed")
                && attempt < 2) { }
        }
        throw new InvalidOperationException("The Node directory changed repeatedly while it was being read.");
    }

    private async Task<NodeListing[]> ReadNodesSnapshotAsync(int protocol, string build,
        string content, CancellationToken cancel)
    {
        NodeDirectoryPage first = await GetNodePageAsync(protocol, build, content, 0, null, cancel)
            .ConfigureAwait(false);
        ValidateDirectoryPage(first, 0, null, protocol, build, content);

        var entries = new List<NodeListing>(first.TotalEntries);
        var seen = new HashSet<Guid>();
        AddDirectoryEntries(first, seen, entries);
        for (int page = 1; page < first.PageCount; page++)
        {
            NodeDirectoryPage next = await GetNodePageAsync(protocol, build, content,
                page, first.Revision, cancel).ConfigureAwait(false);
            ValidateDirectoryPage(next, page, first, protocol, build, content);
            AddDirectoryEntries(next, seen, entries);
        }
        if (entries.Count != first.TotalEntries)
            throw new InvalidOperationException("Backend returned an incomplete Node directory.");
        return entries.ToArray();
    }

    private async Task<NodeDirectoryPage> GetNodePageAsync(int protocol, string build,
        string content, int page, long? revision, CancellationToken cancel)
    {
        string path = $"v1/nodes?protocol={protocol.ToString(CultureInfo.InvariantCulture)}"
            + $"&build={Uri.EscapeDataString(build)}&content={Uri.EscapeDataString(content)}"
            + $"&page={page.ToString(CultureInfo.InvariantCulture)}";
        if (revision is { } pinned)
            path += $"&revision={pinned.ToString(CultureInfo.InvariantCulture)}";
        return await SendAsync<NodeDirectoryPage>(HttpMethod.Get, path, null, null, cancel)
            .ConfigureAwait(false);
    }

    private static void ValidateDirectoryPage(NodeDirectoryPage page, int expectedPage,
        NodeDirectoryPage? first, int protocol, string build, string content)
    {
        if (page is null || page.Revision <= 0 || page.Page != expectedPage
            || page.PageCount is < 1 or > NodeDirectoryContract.MaximumPages
            || page.TotalEntries is < 0 or > NodeDirectoryContract.MaximumEntries
            || page.Entries.IsDefault || page.Entries.Length > NodeDirectoryContract.MaximumPageEntries)
            throw new InvalidOperationException("Backend returned an invalid Node directory page.");
        int expectedPageCount = Math.Max(1, (page.TotalEntries
            + NodeDirectoryContract.MaximumPageEntries - 1)
            / NodeDirectoryContract.MaximumPageEntries);
        int expectedEntries = Math.Min(NodeDirectoryContract.MaximumPageEntries,
            Math.Max(0, page.TotalEntries - expectedPage * NodeDirectoryContract.MaximumPageEntries));
        if (page.PageCount != expectedPageCount || page.Entries.Length != expectedEntries
            || first is { Revision: var revision } && page.Revision != revision
            || first is { PageCount: var pageCount } && page.PageCount != pageCount
            || first is { TotalEntries: var total } && page.TotalEntries != total)
            throw new InvalidOperationException("Backend returned an inconsistent Node directory page.");
        foreach (NodeDirectoryEntry entry in page.Entries)
        {
            if (entry is null || entry.NodeId == Guid.Empty
                || entry.Name is not { Length: >= 1 and <= 64 } || entry.Name.Any(char.IsControl)
                || entry.Region is not { Length: >= 1 and <= 32 } || entry.Region.Any(char.IsControl)
                || !ValidNodeUri(entry.PublicControlUri)
                || entry.ProtocolVersion != protocol || entry.BuildVersion != build
                || entry.ContentHash != content || entry.Capacity is < 1 or > 10000
                || entry.OnlineUsers is < 0 || entry.OnlineUsers > entry.Capacity
                || entry.LobbyCount is < 0 or > 10000 || entry.ActiveMatches is < 0 or > 10000
                || entry.TrustClass is not { Length: >= 1 and <= 32 } || entry.TrustClass.Any(char.IsControl)
                || entry.MapCatalogRevision < 0 || entry.MapCount is < 0 or > 256
                || entry.MapCatalogRevision == 0 && entry.MapCount != 0
                || entry.MapCatalogHash is { Length: > 0 } hash
                    && (hash.Length != 64 || !hash.All(char.IsAsciiHexDigit)))
                throw new InvalidOperationException("Backend returned an invalid Node directory entry.");
        }
    }

    private static void AddDirectoryEntries(NodeDirectoryPage page, HashSet<Guid> seen,
        List<NodeListing> entries)
    {
        foreach (NodeDirectoryEntry entry in page.Entries)
        {
            if (!seen.Add(entry.NodeId))
                throw new InvalidOperationException("Backend returned duplicate Node directory entries.");
            entries.Add(new NodeListing(entry.NodeId, entry.Name, entry.Region,
                entry.PublicControlUri, entry.ProtocolVersion, entry.BuildVersion,
                entry.ContentHash, entry.Capacity, entry.OnlineUsers, entry.LobbyCount,
                entry.ActiveMatches, entry.TrustClass, entry.LastHeartbeat,
                MapCatalogRevision: entry.MapCatalogRevision, MapCount: entry.MapCount,
                MapCatalogHash: entry.MapCatalogHash));
        }
    }
    public async Task<NodeAdmissionTicket> GetNodeTicketAsync(Guid nodeId, CancellationToken cancel = default)
    {
        if (nodeId == Guid.Empty) throw new ArgumentException("A Node identity is required.", nameof(nodeId));
        var ticket = await SendAsync<NodeAdmissionTicket>(HttpMethod.Post, "v1/node-admissions", new { nodeId },
            await AccessTokenAsync(cancel).ConfigureAwait(false), cancel).ConfigureAwait(false);
        return ValidateNodeAdmission(ticket, nodeId);
    }
    public async Task<NodeAdmissionTicket> GetGuestNodeTicketAsync(Guid nodeId, string displayName,
        CancellationToken cancel = default)
    {
        if (nodeId == Guid.Empty) throw new ArgumentException("A Node identity is required.", nameof(nodeId));
        displayName = ValidateGuestDisplayName(displayName);
        var ticket = await SendAsync<NodeAdmissionTicket>(HttpMethod.Post,
            "v1/guest-node-admissions", new { nodeId, displayName }, null, cancel).ConfigureAwait(false);
        return ValidateNodeAdmission(ticket, nodeId);
    }
    private NodeAdmissionTicket ValidateNodeAdmission(NodeAdmissionTicket ticket, Guid nodeId)
    {
        DateTimeOffset now = _time.GetUtcNow();
        if (ticket.NodeId != nodeId || !ValidNodeUri(ticket.PublicControlUri) || ticket.ExpiresAt <= now
            || ticket.ExpiresAt > now.Add(NodeAdmissionContract.Lifetime + NodeAdmissionContract.MaximumClockSkew)
            || ticket.Ticket is not { Length: > 0 and <= 4096 })
            throw new InvalidOperationException("Backend returned an invalid Node admission.");
        return ticket;
    }
    internal static string ValidateGuestDisplayName(string? displayName)
    {
        string value = displayName?.Trim() ?? throw new ArgumentNullException(nameof(displayName));
        if (value is not { Length: >= 1 and <= 16 } || value.Any(c => c is < ' ' or > '~'))
            throw new ArgumentException("Guest display names must be 1 to 16 printable ASCII characters.", nameof(displayName));
        return value;
    }
    internal static bool ValidNodeUri(string? value)
        => NodeEndpointContract.TryValidatePublicControlUri(value, out _);

    internal static bool ValidMapKeys(string[]? mapKeys)
        => mapKeys is null || (mapKeys.Length <= 256
            && mapKeys.All(key => key is { Length: > 0 and <= 128 } && !string.IsNullOrWhiteSpace(key)
                && key.All(c => c is >= ' ' and <= '~'))
            && mapKeys.Distinct(StringComparer.Ordinal).Count() == mapKeys.Length);
}

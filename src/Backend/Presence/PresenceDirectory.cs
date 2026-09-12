using System.Collections.Immutable;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using MphRead.Backend.Nodes;
using ProjectPrime.Server.Shared;

namespace MphRead.Backend.Presence;

public class PresenceDirectoryException(string code, string message, int statusCode)
    : Exception(message)
{
    public string Code { get; } = code;
    public int StatusCode { get; } = statusCode;
}

public sealed class PresenceDirectoryPageException(string code, string message, int statusCode)
    : PresenceDirectoryException(code, message, statusCode);

/// <summary>
/// Ephemeral, Backend-owned projection of the names that Nodes explicitly
/// make public. NodeDirectory remains authoritative for Node liveness and
/// total population; this service never persists presence to PostgreSQL.
/// </summary>
public sealed class PresenceDirectory
{
    public static readonly TimeSpan ReportLifetime = TimeSpan.FromSeconds(45);

    private sealed record StoredReport(Guid Incarnation, long Revision,
        ImmutableArray<NodePresenceEntry> Players, DateTimeOffset ReceivedAt);

    private readonly record struct PopulationKey(Guid NodeId, Guid Incarnation,
        string Region, int Capacity, int OnlineUsers);

    private readonly NodeDirectory _nodes;
    private readonly TimeProvider _clock;
    private readonly ILogger _logger;
    private readonly object _gate = new();
    private readonly Dictionary<Guid, StoredReport> _reports = [];
    private ImmutableArray<PopulationKey> _population = ImmutableArray<PopulationKey>.Empty;
    private long _revision = 1;

    public PresenceDirectory(NodeDirectory nodes, TimeProvider clock,
        ILogger<PresenceDirectory>? logger = null)
    {
        _nodes = nodes;
        _clock = clock;
        _logger = logger ?? NullLogger<PresenceDirectory>.Instance;
    }

    /// <summary>Current public snapshot revision after reconciling fresh Node
    /// population and report TTLs. This is for diagnostics/tests; clients use
    /// the revision returned by <see cref="BrowsePage"/>.</summary>
    public long Revision
    {
        get
        {
            ImmutableArray<NodePresencePopulationSnapshot> population =
                _nodes.SnapshotPresencePopulation();
            lock (_gate)
            {
                ReconcileLocked(population, _clock.GetUtcNow());
                return _revision;
            }
        }
    }

    /// <summary>
    /// Accepts one authenticated report for the currently registered Node
    /// incarnation. A report revision is monotonic per incarnation. An exact
    /// equal revision/payload is an idempotent keepalive and refreshes its TTL
    /// without changing the public directory revision.
    /// </summary>
    public void Report(Guid nodeId, NodePresenceReport report)
    {
        if (nodeId == Guid.Empty)
            throw InvalidReport("The Node identity is invalid.");

        try
        {
            report.Validate();
        }
        catch (ArgumentException error)
        {
            throw InvalidReport(error.Message);
        }

        // Read the authoritative Node directory outside the presence lock so
        // the two services never require a lock in the opposite order.
        ImmutableArray<NodePresencePopulationSnapshot> population =
            _nodes.SnapshotPresencePopulation();
        lock (_gate)
        {
            DateTimeOffset now = _clock.GetUtcNow();
            ReconcileLocked(population, now);
            NodePresencePopulationSnapshot? current = population
                .FirstOrDefault(value => value.NodeId == nodeId);
            if (current == null || current.Incarnation != report.Incarnation)
                throw Stale("The Node presence report belongs to an old incarnation.",
                    "stale_incarnation");
            if (report.Players.Length > current.Capacity)
                throw InvalidReport("The Node presence report exceeds its session capacity.",
                    "presence_capacity_exceeded");

            if (_reports.TryGetValue(nodeId, out StoredReport? previous))
            {
                if (previous.Incarnation != report.Incarnation)
                    throw Stale("The Node presence report belongs to an old incarnation.",
                        "stale_incarnation");
                if (report.Revision < previous.Revision)
                    throw Stale("The Node presence revision is stale.", "stale_revision");
                if (report.Revision == previous.Revision)
                {
                    if (!SamePlayers(previous.Players, report.Players))
                        throw Stale("The Node presence revision has changed payload data.",
                            "stale_revision");

                    // Identical reports are keepalives. Do not churn the
                    // public revision merely because a Node is healthy.
                    _reports[nodeId] = previous with { ReceivedAt = now };
                    BackendDiagnostics.Directory(_logger, "presence-report", "refresh");
                    return;
                }
            }

            long visibleAfter = VisibleCountLocked(population, now, nodeId);
            visibleAfter += report.Players.Length;
            if (visibleAfter > PresenceContract.MaximumEntries)
                throw new PresenceDirectoryException("presence_too_large",
                    "The public presence directory is too large.",
                    StatusCodes.Status413PayloadTooLarge);

            _reports[nodeId] = new StoredReport(report.Incarnation, report.Revision,
                report.Players, now);
            IncrementRevisionLocked();
            BackendDiagnostics.Directory(_logger, "presence-report", "success");
        }
    }

    /// <summary>Compatibility-friendly success-returning alias for callers
    /// that treat publication as a try operation. Rejections remain explicit
    /// <see cref="PresenceDirectoryException"/> failures.</summary>
    public bool Publish(Guid nodeId, NodePresenceReport report)
    {
        Report(nodeId, report);
        return true;
    }

    /// <summary>Reads a deterministic, revision-pinned public page. The total
    /// is freshly summed from NodeDirectory population and is intentionally
    /// not clamped to the number of visible names.</summary>
    public PresenceDirectoryPage BrowsePage(int page = 0, long? revision = null)
    {
        if (page < 0 || page >= PresenceContract.MaximumPages)
            throw InvalidPage("The presence directory page is invalid.");
        if (revision is < 0)
            throw new PresenceDirectoryPageException("invalid_revision",
                "The presence directory revision is invalid.",
                StatusCodes.Status400BadRequest);

        ImmutableArray<NodePresencePopulationSnapshot> population =
            _nodes.SnapshotPresencePopulation();
        lock (_gate)
        {
            DateTimeOffset now = _clock.GetUtcNow();
            ReconcileLocked(population, now);
            if (revision is { } requested && requested != _revision)
                throw new PresenceDirectoryPageException("directory_revision_changed",
                    "The presence directory changed while it was being read.",
                    StatusCodes.Status409Conflict);

            List<(PublicPresenceEntry Entry, Guid NodeId, int Index)> candidates = [];
            foreach (NodePresencePopulationSnapshot node in population)
            {
                if (!_reports.TryGetValue(node.NodeId, out StoredReport? report)
                    || report.Incarnation != node.Incarnation || !Fresh(report, now))
                    continue;
                for (int index = 0; index < report.Players.Length; index++)
                {
                    NodePresenceEntry player = report.Players[index];
                    candidates.Add((new PublicPresenceEntry(player.DisplayName,
                        player.Activity, node.Region), node.NodeId, index));
                }
            }

            if (candidates.Count > PresenceContract.MaximumEntries)
                throw new PresenceDirectoryPageException("presence_too_large",
                    "The public presence directory is too large.",
                    StatusCodes.Status413PayloadTooLarge);

            candidates.Sort(static (left, right) =>
            {
                int result = ((byte)left.Entry.Activity).CompareTo((byte)right.Entry.Activity);
                if (result != 0) return result;
                result = StringComparer.Ordinal.Compare(left.Entry.DisplayName,
                    right.Entry.DisplayName);
                if (result != 0) return result;
                result = StringComparer.Ordinal.Compare(left.Entry.Region, right.Entry.Region);
                if (result != 0) return result;
                result = left.NodeId.CompareTo(right.NodeId);
                return result != 0 ? result : left.Index.CompareTo(right.Index);
            });

            int pageCount = Math.Max(1,
                (candidates.Count + PresenceContract.PageSize - 1) / PresenceContract.PageSize);
            if (page >= pageCount)
                throw InvalidPage("The presence directory page is invalid.");

            long totalOnline = 0;
            foreach (NodePresencePopulationSnapshot node in population)
            {
                if (totalOnline > int.MaxValue - node.OnlineUsers)
                    throw new PresenceDirectoryPageException("presence_too_large",
                        "The online population is too large for the public contract.",
                        StatusCodes.Status413PayloadTooLarge);
                totalOnline += node.OnlineUsers;
            }

            ImmutableArray<PublicPresenceEntry> entries = candidates
                .Skip(page * PresenceContract.PageSize)
                .Take(PresenceContract.PageSize)
                .Select(value => value.Entry)
                .ToImmutableArray();
            var resultPage = new PresenceDirectoryPage(_revision, (int)totalOnline,
                candidates.Count, page, pageCount, entries, now);
            resultPage.Validate();
            BackendDiagnostics.Directory(_logger, "presence-browse", "success");
            return resultPage;
        }
    }

    private void ReconcileLocked(ImmutableArray<NodePresencePopulationSnapshot> population,
        DateTimeOffset now)
    {
        bool changed = UpdatePopulationLocked(population);
        HashSet<Guid> currentNodes = population.Select(value => value.NodeId).ToHashSet();
        foreach ((Guid nodeId, StoredReport report) in _reports.ToArray())
        {
            NodePresencePopulationSnapshot? current = population
                .FirstOrDefault(value => value.NodeId == nodeId);
            if (!currentNodes.Contains(nodeId) || current == null
                || current.Incarnation != report.Incarnation || !Fresh(report, now))
            {
                _reports.Remove(nodeId);
                changed = true;
            }
        }
        if (changed) IncrementRevisionLocked();
    }

    private bool UpdatePopulationLocked(
        ImmutableArray<NodePresencePopulationSnapshot> population)
    {
        ImmutableArray<PopulationKey> next = population
            .Select(value => new PopulationKey(value.NodeId, value.Incarnation,
                value.Region, value.Capacity, value.OnlineUsers))
            .ToImmutableArray();
        if (_population.SequenceEqual(next)) return false;
        _population = next;
        return true;
    }

    private long VisibleCountLocked(ImmutableArray<NodePresencePopulationSnapshot> population,
        DateTimeOffset now, Guid replacingNode)
    {
        long count = 0;
        foreach (NodePresencePopulationSnapshot node in population)
        {
            if (node.NodeId == replacingNode) continue;
            if (_reports.TryGetValue(node.NodeId, out StoredReport? report)
                && report.Incarnation == node.Incarnation && Fresh(report, now))
                count += report.Players.Length;
        }
        return count;
    }

    private static bool SamePlayers(ImmutableArray<NodePresenceEntry> left,
        ImmutableArray<NodePresenceEntry> right)
    {
        if (left.Length != right.Length) return false;
        for (int index = 0; index < left.Length; index++)
        {
            if (left[index] is null || right[index] is null
                || left[index].DisplayName != right[index].DisplayName
                || left[index].Activity != right[index].Activity) return false;
        }
        return true;
    }

    private static bool Fresh(StoredReport report, DateTimeOffset now)
        => now - report.ReceivedAt < ReportLifetime;

    private static PresenceDirectoryPageException InvalidPage(string message)
        => new("invalid_page", message, StatusCodes.Status400BadRequest);

    private static PresenceDirectoryException InvalidReport(string message,
        string code = "invalid_request")
        => new(code, message, StatusCodes.Status400BadRequest);

    private static PresenceDirectoryException Stale(string message, string code)
        => new(code, message, StatusCodes.Status409Conflict);

    private void IncrementRevisionLocked()
    {
        if (_revision < long.MaxValue) _revision++;
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace MphRead.Mods.MapGen;

/// <summary>
/// One immutable package in a map's server-assigned revision history.
/// The package hash remains the multiplayer/install identity; the revision number
/// is presentation metadata scoped to a single MapId.
/// </summary>
public sealed record CommunityMapRevision(
    int RevisionNumber,
    string Hash,
    string? ParentHash,
    string CreatedBy,
    DateTimeOffset CreatedAt,
    CommunityMap Package);

/// <summary>
/// Map-level catalog view. CurrentRevision is the package selected for public
/// discovery/play; LatestRevision is the newest revision visible to the caller.
/// They may differ while a creator has an unlisted or draft revision.
/// </summary>
public sealed record CommunityMapProject(
    Guid MapId,
    string OwnerId,
    string Name,
    string? DisplayName,
    string? Author,
    string? CurrentHash,
    string LatestHash,
    int RevisionCount,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    CommunityMapRevision? CurrentRevision,
    CommunityMapRevision LatestRevision);

internal sealed record CommunityMapRevisionState(
    string Hash,
    Guid MapId,
    int RevisionNumber,
    string? ParentHash,
    string CreatedBy,
    DateTimeOffset CreatedAt);

internal sealed record CommunityMapProjectState(
    Guid MapId,
    string OwnerId,
    string? CurrentHash,
    string LatestHash,
    int NextRevisionNumber,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

internal sealed record CommunityMapCatalogState(
    int Format,
    CommunityMapProjectState[] Projects,
    CommunityMapRevisionState[] Revisions);

/// <summary>
/// Persistent map-level catalog layered over immutable .ppmap archives.
///
/// Existing package metadata remains authoritative for visibility and runtime
/// compatibility. This catalog adds stable server revision numbers, parent links,
/// and a current-package pointer without changing package hashes or the legacy API.
/// </summary>
internal sealed class MapCommunityRevisionCatalog
{
    private const int Format = 1;
    private const string FileName = "map_catalog_v2.json";
    private readonly string _path;
    private readonly object _gate = new();
    private readonly Dictionary<Guid, CommunityMapProjectState> _projects = new();
    private readonly Dictionary<string, CommunityMapRevisionState> _revisions =
        new(StringComparer.Ordinal);

    public MapCommunityRevisionCatalog(string storage, IEnumerable<CommunityMap> packages)
    {
        _path = Path.Combine(storage, FileName);
        Load();
        Reconcile(packages);
    }

    public int ProjectCount
    {
        get { lock (_gate) return _projects.Count; }
    }

    public CommunityMapRevision Register(CommunityMap package, string creatorId)
    {
        lock (_gate)
        {
            if (_revisions.TryGetValue(package.Hash, out CommunityMapRevisionState? existing))
                return Attach(existing, package);

            DateTimeOffset created = package.PublishedAt == default
                ? DateTimeOffset.UtcNow : package.PublishedAt;

            if (!_projects.TryGetValue(package.MapId, out CommunityMapProjectState? project))
            {
                project = new CommunityMapProjectState(
                    package.MapId,
                    package.OwnerId,
                    package.Listed && !package.Draft ? package.Hash : null,
                    package.Hash,
                    2,
                    created,
                    created);
                _projects[package.MapId] = project;

                var first = new CommunityMapRevisionState(
                    package.Hash, package.MapId, 1, null, creatorId, created);
                _revisions[package.Hash] = first;
                Save();
                return Attach(first, package);
            }

            int revisionNumber = Math.Max(1, project.NextRevisionNumber);
            string? parent = _revisions.ContainsKey(project.LatestHash)
                ? project.LatestHash : null;
            var revision = new CommunityMapRevisionState(
                package.Hash, package.MapId, revisionNumber, parent, creatorId, created);
            _revisions[package.Hash] = revision;

            string? current = package.Listed && !package.Draft
                ? package.Hash : project.CurrentHash;
            _projects[package.MapId] = project with
            {
                CurrentHash = current,
                LatestHash = package.Hash,
                NextRevisionNumber = revisionNumber + 1,
                UpdatedAt = created > project.UpdatedAt ? created : DateTimeOffset.UtcNow
            };
            Save();
            return Attach(revision, package);
        }
    }

    public void ApplyVisibility(CommunityMap package, IEnumerable<CommunityMap> packages)
    {
        lock (_gate)
        {
            if (!_projects.TryGetValue(package.MapId, out CommunityMapProjectState? project))
            {
                ReconcileLocked(packages);
                Save();
                return;
            }

            string? current = project.CurrentHash;
            if (package.Listed && !package.Draft)
            {
                current = package.Hash;
            }
            else if (current == package.Hash)
            {
                var available = packages.ToDictionary(m => m.Hash, StringComparer.Ordinal);
                current = _revisions.Values
                    .Where(r => r.MapId == package.MapId
                        && available.TryGetValue(r.Hash, out CommunityMap? item)
                        && item.Listed && !item.Draft)
                    .OrderByDescending(r => r.RevisionNumber)
                    .Select(r => r.Hash)
                    .FirstOrDefault();
            }

            _projects[package.MapId] = project with
            {
                CurrentHash = current,
                UpdatedAt = DateTimeOffset.UtcNow
            };
            Save();
        }
    }

    public CommunityMapProject? BuildProject(
        Guid mapId, IEnumerable<CommunityMap> visiblePackages)
    {
        lock (_gate)
        {
            if (!_projects.TryGetValue(mapId, out CommunityMapProjectState? project))
                return null;

            var available = visiblePackages
                .Where(m => m.MapId == mapId)
                .ToDictionary(m => m.Hash, StringComparer.Ordinal);
            var revisions = _revisions.Values
                .Where(r => r.MapId == mapId && available.ContainsKey(r.Hash))
                .OrderBy(r => r.RevisionNumber)
                .Select(r => Attach(r, available[r.Hash]))
                .ToArray();
            if (revisions.Length == 0) return null;

            CommunityMapRevision latest = revisions[^1];
            CommunityMapRevision? current = project.CurrentHash != null
                ? revisions.FirstOrDefault(r => r.Hash == project.CurrentHash)
                : null;
            CommunityMap presentation = current?.Package ?? latest.Package;
            DateTimeOffset updated = latest.CreatedAt > project.UpdatedAt
                ? latest.CreatedAt : project.UpdatedAt;

            return new CommunityMapProject(
                mapId,
                project.OwnerId,
                presentation.Name,
                presentation.DisplayName,
                presentation.Author,
                current?.Hash,
                latest.Hash,
                revisions.Length,
                project.CreatedAt,
                updated,
                current,
                latest);
        }
    }

    public CommunityMapRevision[] BuildRevisions(
        Guid mapId, IEnumerable<CommunityMap> visiblePackages)
    {
        lock (_gate)
        {
            var available = visiblePackages
                .Where(m => m.MapId == mapId)
                .ToDictionary(m => m.Hash, StringComparer.Ordinal);
            return _revisions.Values
                .Where(r => r.MapId == mapId && available.ContainsKey(r.Hash))
                .OrderByDescending(r => r.RevisionNumber)
                .Select(r => Attach(r, available[r.Hash]))
                .ToArray();
        }
    }

    private static CommunityMapRevision Attach(
        CommunityMapRevisionState state, CommunityMap package)
        => new(state.RevisionNumber, state.Hash, state.ParentHash,
            state.CreatedBy, state.CreatedAt, package);

    private void Load()
    {
        if (!File.Exists(_path)) return;
        try
        {
            if (new FileInfo(_path).Length > 32 * 1024 * 1024)
                throw new InvalidDataException("Community v2 catalog exceeds storage budget.");
            var state = JsonSerializer.Deserialize<CommunityMapCatalogState>(
                File.ReadAllBytes(_path), MapPackageReader.JsonOptions);
            if (state == null || state.Format != Format
                || state.Projects == null || state.Revisions == null)
                throw new InvalidDataException("Invalid Community v2 catalog.");

            foreach (CommunityMapRevisionState revision in state.Revisions)
            {
                if (revision.MapId == Guid.Empty || revision.RevisionNumber < 1
                    || !MapCommunityClient.ValidHash(revision.Hash)
                    || revision.ParentHash != null && !MapCommunityClient.ValidHash(revision.ParentHash)
                    || string.IsNullOrWhiteSpace(revision.CreatedBy)
                    || !_revisions.TryAdd(revision.Hash, revision))
                    throw new InvalidDataException("Invalid Community revision catalog.");
            }

            foreach (CommunityMapProjectState project in state.Projects)
            {
                if (project.MapId == Guid.Empty
                    || string.IsNullOrWhiteSpace(project.OwnerId)
                    || !MapCommunityClient.ValidHash(project.LatestHash)
                    || project.CurrentHash != null && !MapCommunityClient.ValidHash(project.CurrentHash)
                    || project.NextRevisionNumber < 2
                    || !_projects.TryAdd(project.MapId, project))
                    throw new InvalidDataException("Invalid Community project catalog.");
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException or InvalidDataException)
        {
            // Package files and per-package metadata remain authoritative. A
            // damaged derived catalog must not make the Community service fail
            // to start; rebuilding preserves package availability.
            Console.Error.WriteLine("[maphub] Rebuilding Community v2 catalog: " + ex.Message);
            _projects.Clear();
            _revisions.Clear();
        }
    }

    private void Reconcile(IEnumerable<CommunityMap> packages)
    {
        lock (_gate)
        {
            ReconcileLocked(packages);
            Save();
        }
    }

    private void ReconcileLocked(IEnumerable<CommunityMap> packages)
    {
        CommunityMap[] availablePackages = packages
            .GroupBy(m => m.Hash, StringComparer.Ordinal)
            .Select(g => g.First())
            .ToArray();
        var availableByHash = availablePackages.ToDictionary(m => m.Hash, StringComparer.Ordinal);
        var validRevisions = _revisions.Values
            .Where(r => availableByHash.TryGetValue(r.Hash, out CommunityMap? package)
                && package.MapId == r.MapId)
            .ToDictionary(r => r.Hash, StringComparer.Ordinal);
        _revisions.Clear();
        foreach (var pair in validRevisions) _revisions[pair.Key] = pair.Value;

        var nextProjects = new Dictionary<Guid, CommunityMapProjectState>();
        foreach (IGrouping<Guid, CommunityMap> group in availablePackages.GroupBy(m => m.MapId))
        {
            CommunityMapProjectState? previous = _projects.GetValueOrDefault(group.Key);
            var existing = _revisions.Values
                .Where(r => r.MapId == group.Key)
                .OrderBy(r => r.RevisionNumber)
                .ToList();
            int next = existing.Count == 0 ? 1 : existing.Max(r => r.RevisionNumber) + 1;
            string? parent = existing.LastOrDefault()?.Hash;

            foreach (CommunityMap package in group
                .Where(m => !_revisions.ContainsKey(m.Hash))
                .OrderBy(m => m.PublishedAt == default ? DateTimeOffset.UnixEpoch : m.PublishedAt)
                .ThenBy(m => m.Hash, StringComparer.Ordinal))
            {
                DateTimeOffset created = package.PublishedAt == default
                    ? DateTimeOffset.UnixEpoch : package.PublishedAt;
                var revision = new CommunityMapRevisionState(
                    package.Hash, package.MapId, next++, parent,
                    package.OwnerId, created);
                _revisions[package.Hash] = revision;
                existing.Add(revision);
                parent = package.Hash;
            }

            existing = existing.OrderBy(r => r.RevisionNumber).ToList();
            if (existing.Count == 0) continue;

            string latestHash = existing[^1].Hash;
            string? current = previous?.CurrentHash;
            if (current == null
                || !availableByHash.TryGetValue(current, out CommunityMap? currentPackage)
                || currentPackage.MapId != group.Key
                || !currentPackage.Listed || currentPackage.Draft)
            {
                current = existing
                    .Where(r => availableByHash[r.Hash].Listed && !availableByHash[r.Hash].Draft)
                    .OrderByDescending(r => r.RevisionNumber)
                    .Select(r => r.Hash)
                    .FirstOrDefault();
            }

            DateTimeOffset createdAt = existing.Min(r => r.CreatedAt);
            DateTimeOffset updatedAt = existing.Max(r => r.CreatedAt);
            string owner = previous?.OwnerId
                ?? group.OrderBy(m => m.PublishedAt == default ? DateTimeOffset.UnixEpoch : m.PublishedAt)
                    .ThenBy(m => m.Hash, StringComparer.Ordinal)
                    .First().OwnerId;

            nextProjects[group.Key] = new CommunityMapProjectState(
                group.Key,
                owner,
                current,
                latestHash,
                existing.Max(r => r.RevisionNumber) + 1,
                previous?.CreatedAt ?? createdAt,
                previous is not null && previous.UpdatedAt > updatedAt
                    ? previous.UpdatedAt : updatedAt);
        }

        _projects.Clear();
        foreach (var pair in nextProjects) _projects[pair.Key] = pair.Value;
    }

    private void Save()
    {
        var state = new CommunityMapCatalogState(
            Format,
            _projects.Values.OrderBy(p => p.MapId).ToArray(),
            _revisions.Values.OrderBy(r => r.MapId).ThenBy(r => r.RevisionNumber).ToArray());
        AtomicFile.Write(_path, JsonSerializer.SerializeToUtf8Bytes(
            state, MapPackageReader.JsonOptions));
    }
}

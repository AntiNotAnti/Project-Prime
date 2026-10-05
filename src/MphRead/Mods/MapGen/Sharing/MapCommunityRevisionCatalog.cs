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
    string? CreatedBy,
    DateTimeOffset CreatedAt,
    CommunityMap Package,
    string? ReleaseNotes = null,
    DateTimeOffset? DeletedAt = null,
    DateTimeOffset? DeleteAfter = null);

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
    CommunityMapRevision LatestRevision,
    DateTimeOffset? ArchivedAt = null,
    DateTimeOffset? DeletedAt = null,
    DateTimeOffset? DeleteAfter = null);

internal sealed record CommunityMapRevisionState(
    string Hash,
    Guid MapId,
    int RevisionNumber,
    string? ParentHash,
    string CreatedBy,
    DateTimeOffset CreatedAt,
    string? ReleaseNotes = null,
    DateTimeOffset? DeletedAt = null,
    DateTimeOffset? DeleteAfter = null);

internal sealed record CommunityMapProjectState(
    Guid MapId,
    string OwnerId,
    string? CurrentHash,
    string LatestHash,
    int NextRevisionNumber,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? ArchivedAt = null,
    DateTimeOffset? DeletedAt = null,
    DateTimeOffset? DeleteAfter = null);

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

    public CommunityMapRevision Register(CommunityMap package, string creatorId,
        string? parentHash = null, string? releaseNotes = null,
        bool useProvidedParent = false)
    {
        lock (_gate)
        {
            if (_revisions.TryGetValue(package.Hash, out CommunityMapRevisionState? existing))
                return Attach(existing, package, revealCreatorIdentity: true);

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
                    package.Hash, package.MapId, 1, null, creatorId, created,
                    NormalizeReleaseNotes(releaseNotes));
                _revisions[package.Hash] = first;
                Save();
                return Attach(first, package, revealCreatorIdentity: true);
            }

            int revisionNumber = Math.Max(1, project.NextRevisionNumber);
            string? parent = useProvidedParent
                ? parentHash
                : _revisions.ContainsKey(project.LatestHash) ? project.LatestHash : null;
            if (parent != null && (!_revisions.TryGetValue(parent, out var parentRevision)
                || parentRevision.MapId != package.MapId))
                throw new InvalidDataException("Community revision parent does not belong to this map.");
            var revision = new CommunityMapRevisionState(
                package.Hash, package.MapId, revisionNumber, parent, creatorId, created,
                NormalizeReleaseNotes(releaseNotes));
            _revisions[package.Hash] = revision;

            string? current = package.Listed && !package.Draft
                ? package.Hash : project.CurrentHash;
            _projects[package.MapId] = project with
            {
                CurrentHash = current,
                LatestHash = package.Hash,
                NextRevisionNumber = revisionNumber + 1,
                UpdatedAt = package.Listed && !package.Draft
                    ? (created > project.UpdatedAt ? created : DateTimeOffset.UtcNow)
                    : project.UpdatedAt
            };
            Save();
            return Attach(revision, package, revealCreatorIdentity: true);
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

            bool publicPointerChanged = current != project.CurrentHash;
            _projects[package.MapId] = project with
            {
                CurrentHash = current,
                UpdatedAt = publicPointerChanged || package.Listed && !package.Draft
                    ? DateTimeOffset.UtcNow : project.UpdatedAt
            };
            Save();
        }
    }

    public CommunityMapProject? BuildProject(
        Guid mapId, IEnumerable<CommunityMap> visiblePackages,
        bool revealCreatorIdentity = false, bool includeDeleted = false)
    {
        lock (_gate)
        {
            if (!_projects.TryGetValue(mapId, out CommunityMapProjectState? project))
                return null;

            var available = visiblePackages
                .Where(m => m.MapId == mapId)
                .ToDictionary(m => m.Hash, StringComparer.Ordinal);
            var revisions = _revisions.Values
                .Where(r => r.MapId == mapId && available.ContainsKey(r.Hash)
                    && (includeDeleted || r.DeletedAt == null))
                .OrderBy(r => r.RevisionNumber)
                .Select(r => Attach(r, available[r.Hash], revealCreatorIdentity))
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
                latest,
                project.ArchivedAt,
                project.DeletedAt,
                project.DeleteAfter);
        }
    }

    public CommunityMapRevision[] BuildRevisions(
        Guid mapId, IEnumerable<CommunityMap> visiblePackages,
        bool revealCreatorIdentity = false, bool includeDeleted = false)
    {
        lock (_gate)
        {
            var available = visiblePackages
                .Where(m => m.MapId == mapId)
                .ToDictionary(m => m.Hash, StringComparer.Ordinal);
            return _revisions.Values
                .Where(r => r.MapId == mapId && available.ContainsKey(r.Hash)
                    && (includeDeleted || r.DeletedAt == null))
                .OrderByDescending(r => r.RevisionNumber)
                .Select(r => Attach(r, available[r.Hash], revealCreatorIdentity))
                .ToArray();
        }
    }

    public CommunityMap LegacyPresentation(CommunityMap package)
    {
        lock (_gate)
        {
            if (_projects.TryGetValue(package.MapId, out var project)
                && project.CurrentHash == package.Hash
                && project.UpdatedAt > package.PublishedAt)
                return package with { PublishedAt = project.UpdatedAt };
            return package;
        }
    }

    public string? LatestHash(Guid mapId)
    {
        lock (_gate)
            return _projects.TryGetValue(mapId, out var project)
                ? project.LatestHash : null;
    }

    public string? CurrentHash(Guid mapId)
    {
        lock (_gate)
            return _projects.TryGetValue(mapId, out var project)
                ? project.CurrentHash : null;
    }

    public int? LatestRevisionNumber(Guid mapId)
    {
        lock (_gate)
        {
            if (!_projects.TryGetValue(mapId, out var project)
                || !_revisions.TryGetValue(project.LatestHash, out var revision))
                return null;
            return revision.RevisionNumber;
        }
    }

    public bool ContainsRevision(Guid mapId, string hash)
    {
        lock (_gate)
            return _revisions.TryGetValue(hash, out var revision)
                && revision.MapId == mapId
                && revision.DeletedAt == null;
    }

    public string? RevisionHash(Guid mapId, int revisionNumber)
    {
        lock (_gate)
            return _revisions.Values.FirstOrDefault(r =>
                r.MapId == mapId && r.RevisionNumber == revisionNumber)?.Hash;
    }

    public void Promote(Guid mapId, string hash)
    {
        lock (_gate)
        {
            if (!_projects.TryGetValue(mapId, out var project)
                || !_revisions.TryGetValue(hash, out var revision)
                || revision.MapId != mapId)
                throw new InvalidDataException("Unknown Community map revision.");
            _projects[mapId] = project with
            {
                CurrentHash = hash,
                UpdatedAt = DateTimeOffset.UtcNow
            };
            Save();
        }
    }

    private static string? NormalizeReleaseNotes(string? value)
    {
        value = value?.Trim();
        if (string.IsNullOrEmpty(value)) return null;
        if (value.Length > 4000)
            throw new InvalidDataException("Release notes exceed 4,000 characters.");
        return value;
    }

    public bool IsProjectDiscoverable(Guid mapId)
    {
        lock (_gate)
            return !_projects.TryGetValue(mapId, out var project)
                || project.ArchivedAt == null && project.DeletedAt == null;
    }

    public bool IsProjectDeleted(Guid mapId)
    {
        lock (_gate)
            return _projects.TryGetValue(mapId, out var project)
                && project.DeletedAt != null;
    }

    public bool IsRevisionDeleted(string hash)
    {
        lock (_gate)
            return _revisions.TryGetValue(hash, out var revision)
                && revision.DeletedAt != null;
    }

    public CommunityMapProjectState? ProjectState(Guid mapId)
    {
        lock (_gate) return _projects.GetValueOrDefault(mapId);
    }

    public bool Archive(Guid mapId)
    {
        lock (_gate)
        {
            if (!_projects.TryGetValue(mapId, out var project)
                || project.DeletedAt != null)
                return false;
            if (project.ArchivedAt != null) return true;
            _projects[mapId] = project with
            {
                ArchivedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            };
            Save();
            return true;
        }
    }

    public bool DeleteMap(Guid mapId, TimeSpan retention)
    {
        lock (_gate)
        {
            if (!_projects.TryGetValue(mapId, out var project))
                return false;
            if (project.DeletedAt != null) return true;
            DateTimeOffset now = DateTimeOffset.UtcNow;
            _projects[mapId] = project with
            {
                ArchivedAt = project.ArchivedAt ?? now,
                DeletedAt = now,
                DeleteAfter = now.Add(retention),
                UpdatedAt = now
            };
            Save();
            return true;
        }
    }

    public bool RestoreMap(Guid mapId)
    {
        lock (_gate)
        {
            if (!_projects.TryGetValue(mapId, out var project))
                return false;
            if (project.DeleteAfter is DateTimeOffset deadline
                && deadline <= DateTimeOffset.UtcNow)
                return false;
            _projects[mapId] = project with
            {
                ArchivedAt = null,
                DeletedAt = null,
                DeleteAfter = null,
                UpdatedAt = DateTimeOffset.UtcNow
            };
            Save();
            return true;
        }
    }

    public bool DeleteRevision(Guid mapId, int revisionNumber, TimeSpan retention,
        IEnumerable<CommunityMap> packages)
    {
        lock (_gate)
        {
            CommunityMapRevisionState? target = _revisions.Values.FirstOrDefault(r =>
                r.MapId == mapId && r.RevisionNumber == revisionNumber);
            if (target == null) return false;
            if (target.DeletedAt != null) return true;

            int active = _revisions.Values.Count(r =>
                r.MapId == mapId && r.DeletedAt == null);
            if (active <= 1)
                throw new InvalidOperationException(
                    "Delete the map instead of deleting its only remaining revision.");

            DateTimeOffset now = DateTimeOffset.UtcNow;
            _revisions[target.Hash] = target with
            {
                DeletedAt = now,
                DeleteAfter = now.Add(retention)
            };
            RecomputePointersLocked(mapId, packages);
            Save();
            return true;
        }
    }

    public bool RestoreRevision(Guid mapId, int revisionNumber,
        IEnumerable<CommunityMap> packages)
    {
        lock (_gate)
        {
            CommunityMapRevisionState? target = _revisions.Values.FirstOrDefault(r =>
                r.MapId == mapId && r.RevisionNumber == revisionNumber);
            if (target == null) return false;
            if (target.DeleteAfter is DateTimeOffset deadline
                && deadline <= DateTimeOffset.UtcNow)
                return false;
            _revisions[target.Hash] = target with
            {
                DeletedAt = null,
                DeleteAfter = null
            };
            RecomputePointersLocked(mapId, packages);
            Save();
            return true;
        }
    }

    public string[] PurgeCandidates(DateTimeOffset now)
    {
        lock (_gate)
        {
            var result = new HashSet<string>(StringComparer.Ordinal);
            foreach (var project in _projects.Values.Where(p =>
                p.DeleteAfter is DateTimeOffset deadline && deadline <= now))
            {
                foreach (var revision in _revisions.Values.Where(r =>
                    r.MapId == project.MapId))
                    result.Add(revision.Hash);
            }
            foreach (var revision in _revisions.Values.Where(r =>
                r.DeleteAfter is DateTimeOffset deadline && deadline <= now))
                result.Add(revision.Hash);
            return result.ToArray();
        }
    }

    public string[] MapHashes(Guid mapId)
    {
        lock (_gate)
            return _revisions.Values.Where(r => r.MapId == mapId)
                .Select(r => r.Hash).ToArray();
    }

    public CommunityMapRevisionState? RevisionState(Guid mapId, int revisionNumber)
    {
        lock (_gate)
            return _revisions.Values.FirstOrDefault(r =>
                r.MapId == mapId && r.RevisionNumber == revisionNumber);
    }

    public string? RevisionHashForLifecycle(Guid mapId, int revisionNumber)
    {
        lock (_gate)
            return _revisions.Values.FirstOrDefault(r =>
                r.MapId == mapId && r.RevisionNumber == revisionNumber)?.Hash;
    }

    public void RemovePurgedHashes(IEnumerable<string> hashes,
        IEnumerable<CommunityMap> packages)
    {
        lock (_gate)
        {
            var removed = hashes.ToHashSet(StringComparer.Ordinal);
            Guid[] affected = _revisions.Values
                .Where(r => removed.Contains(r.Hash))
                .Select(r => r.MapId)
                .Distinct()
                .ToArray();
            foreach (string hash in removed) _revisions.Remove(hash);

            foreach (Guid mapId in affected)
            {
                if (!_revisions.Values.Any(r => r.MapId == mapId))
                    _projects.Remove(mapId);
                else
                    RecomputePointersLocked(mapId, packages);
            }
            Save();
        }
    }

    private void RecomputePointersLocked(Guid mapId,
        IEnumerable<CommunityMap> packages)
    {
        if (!_projects.TryGetValue(mapId, out var project)) return;
        var packageByHash = packages.Where(m => m.MapId == mapId)
            .ToDictionary(m => m.Hash, StringComparer.Ordinal);
        CommunityMapRevisionState[] active = _revisions.Values
            .Where(r => r.MapId == mapId && r.DeletedAt == null
                && packageByHash.ContainsKey(r.Hash))
            .OrderBy(r => r.RevisionNumber)
            .ToArray();
        if (active.Length == 0) return;

        string latest = active[^1].Hash;
        string? current = project.CurrentHash;
        if (current == null
            || !active.Any(r => r.Hash == current)
            || !packageByHash[current].Listed
            || packageByHash[current].Draft)
        {
            current = active
                .Where(r => packageByHash[r.Hash].Listed
                    && !packageByHash[r.Hash].Draft)
                .OrderByDescending(r => r.RevisionNumber)
                .Select(r => r.Hash)
                .FirstOrDefault();
        }

        _projects[mapId] = project with
        {
            LatestHash = latest,
            CurrentHash = current,
            UpdatedAt = DateTimeOffset.UtcNow
        };
    }

    private static CommunityMapRevision Attach(
        CommunityMapRevisionState state, CommunityMap package,
        bool revealCreatorIdentity)
        => new(state.RevisionNumber, state.Hash, state.ParentHash,
            revealCreatorIdentity ? state.CreatedBy : null, state.CreatedAt, package,
            state.ReleaseNotes, state.DeletedAt, state.DeleteAfter);

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

            if (_revisions.Values.Any(r =>
                r.DeleteAfter != null && r.DeletedAt == null))
                throw new InvalidDataException("Community revision deletion state is invalid.");

            if (_revisions.Values.GroupBy(r => r.MapId).Any(group =>
                group.Select(r => r.RevisionNumber).Distinct().Count() != group.Count()))
                throw new InvalidDataException("Community revision numbers are not unique.");

            foreach (CommunityMapProjectState project in state.Projects)
            {
                if (project.DeleteAfter != null && project.DeletedAt == null)
                    throw new InvalidDataException("Community project deletion state is invalid.");
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
                    package.OwnerId, created, null);
                _revisions[package.Hash] = revision;
                existing.Add(revision);
                parent = package.Hash;
            }

            existing = existing.OrderBy(r => r.RevisionNumber).ToList();
            if (existing.Count == 0) continue;
            var activeExisting = existing.Where(r => r.DeletedAt == null).ToArray();
            if (activeExisting.Length == 0) activeExisting = existing.ToArray();

            string latestHash = activeExisting[^1].Hash;
            string? current = previous?.CurrentHash;
            if (current == null
                || !activeExisting.Any(r => r.Hash == current)
                || !availableByHash.TryGetValue(current, out CommunityMap? currentPackage)
                || currentPackage.MapId != group.Key
                || !currentPackage.Listed || currentPackage.Draft)
            {
                current = activeExisting
                    .Where(r => availableByHash[r.Hash].Listed && !availableByHash[r.Hash].Draft)
                    .OrderByDescending(r => r.RevisionNumber)
                    .Select(r => r.Hash)
                    .FirstOrDefault();
            }

            DateTimeOffset createdAt = existing.Min(r => r.CreatedAt);
            var publicRevisions = activeExisting
                .Where(r => availableByHash[r.Hash].Listed && !availableByHash[r.Hash].Draft)
                .ToArray();
            DateTimeOffset derivedUpdatedAt = publicRevisions.Length > 0
                ? publicRevisions.Max(r => r.CreatedAt)
                : existing.Max(r => r.CreatedAt);
            DateTimeOffset updatedAt = previous is not null
                && previous.UpdatedAt > derivedUpdatedAt
                    ? previous.UpdatedAt : derivedUpdatedAt;
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
                updatedAt,
                previous?.ArchivedAt,
                previous?.DeletedAt,
                previous?.DeleteAfter);
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

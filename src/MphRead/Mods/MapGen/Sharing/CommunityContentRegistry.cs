using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace MphRead.Mods.MapGen;

internal sealed record CommunityContentStoredProject(
    string Kind, Guid ContentId, string OwnerId,
    string Name, string? DisplayName, string? Author,
    string? Description, string[] Tags,
    string? CurrentHash, string LatestHash,
    DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt,
    CommunityContentRevision[] Revisions, bool Archived = false);

internal sealed record CommunityContentStoredState(
    int SchemaVersion, CommunityContentStoredProject[] Projects);

/// <summary>
/// Private, atomically persisted metadata for non-map Community content kinds.
/// The existing v2 map revision catalog remains the one authority for maps.
/// All writers must supply verified immutable local bytes; PR-09 will expose
/// creator upload endpoints after content-type-specific validation.
/// </summary>
public sealed class CommunityContentRegistry
{
    private const int Format = 1;
    private const int MaximumProjects = 4096;
    private const int MaximumRevisions = 16384;
    private const int MaximumTags = 16;
    private const int MaximumDependencies = 32;
    private readonly string _root;
    private readonly string _objects;
    private readonly string _index;
    private readonly object _gate = new();
    private readonly Dictionary<(string Kind, Guid Id), CommunityContentStoredProject> _projects
        = new();

    public CommunityContentRegistry(string storage)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(storage);
        _root = Path.GetFullPath(storage);
        _objects = Path.Combine(_root, "content-v3");
        _index = Path.Combine(_root, "content_catalog_v3.json");
        Directory.CreateDirectory(_objects);

        if (!File.Exists(_index)) return;
        if (new FileInfo(_index).Length > 32L * 1024 * 1024)
            throw new InvalidDataException("V3 content catalog exceeds its metadata budget.");
        var saved = JsonSerializer.Deserialize<CommunityContentStoredState>(
            File.ReadAllBytes(_index), MapPackageReader.JsonOptions)
            ?? throw new InvalidDataException("Missing v3 catalog state.");
        if (saved.SchemaVersion != Format || saved.Projects == null
            || saved.Projects.Length > MaximumProjects
            || saved.Projects.Sum(x => x?.Revisions?.Length ?? 0) > MaximumRevisions)
            throw new InvalidDataException("Invalid v3 catalog schema or bounds.");
        foreach (CommunityContentStoredProject project in saved.Projects)
        {
            ValidateProject(project);
            if (!_projects.TryAdd((project.Kind, project.ContentId), project))
                throw new InvalidDataException("Duplicate v3 content project identity.");
        }
    }

    /// <summary>
    /// Trusted, local service-side registration only. This method deliberately
    /// does not authenticate remote users and MUST NOT be exposed as an HTTP
    /// route until PR-09 validates type-specific files and creator permissions.
    /// No code path can register a map here.
    /// </summary>
    public CommunityContentProject RegisterVerifiedBlob(string stagedFile,
        CommunityContentRegistration metadata)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        ValidateRegistration(metadata);
        if (string.IsNullOrWhiteSpace(stagedFile))
            throw new ArgumentException("A verified local asset path is required.", nameof(stagedFile));
        using (var source = new FileStream(stagedFile, FileMode.Open,
            FileAccess.Read, FileShare.Read, 65536, FileOptions.SequentialScan))
        {
            if (source.Length is <= 0 or > MapPackageReader.MaxEntryBytes)
                throw new InvalidDataException("V3 asset exceeds the 256 MiB file budget.");
        }
        long bytes = new FileInfo(stagedFile).Length;
        string hash = MapBuildFingerprint.HashFile(stagedFile);
        if (!MapCommunityClient.ValidHash(hash))
            throw new InvalidDataException("V3 immutable file digest is invalid.");

        lock (_gate)
        {
            var key = (metadata.Kind, metadata.ContentId);
            bool existed = _projects.TryGetValue(key, out var current);
            if (!existed && _projects.Count >= MaximumProjects)
                throw new InvalidDataException("V3 catalog project budget exceeded.");
            if (current != null && current.OwnerId != metadata.OwnerId)
                throw new InvalidDataException("Cannot change v3 content owner.");
            if (current?.Revisions.Any(r => r.ContentHash == hash) == true)
                return Project(current, owner: true)
                    ?? throw new InvalidDataException("Existing asset metadata is invalid.");

            int count = _projects.Values.Sum(p => p.Revisions.Length);
            if (count >= MaximumRevisions)
                throw new InvalidDataException("V3 catalog revision budget exceeded.");
            if (current == null && metadata.ExpectedParentHash != null)
                throw new InvalidDataException("New v3 content cannot have a parent.");
            if (current != null
                && metadata.ExpectedParentHash != current.LatestHash)
                throw new InvalidDataException("Stale v3 content parent; reload before publishing.");

            // Immutable objects are persisted before the metadata pointer.
            // If metadata persistence fails, an unreferenced blob is harmless,
            // while no catalog revision ever points to an unwritten object.
            PutVerifiedBlob(stagedFile, hash, bytes);

            DateTimeOffset now = DateTimeOffset.UtcNow;
            int revisionNumber = (current?.Revisions.LastOrDefault()?.RevisionNumber ?? 0) + 1;
            var revision = new CommunityContentRevision(
                revisionNumber, hash, ArchiveHash: null, bytes,
                metadata.MimeType, metadata.Visibility, now,
                ParentHash: current?.LatestHash,
                ReleaseNotes: metadata.ReleaseNotes,
                Dependencies: metadata.Dependencies?.ToArray());
            var next = new CommunityContentStoredProject(
                metadata.Kind, metadata.ContentId, metadata.OwnerId,
                metadata.Name, metadata.DisplayName, metadata.Author,
                metadata.Description, metadata.Tags.ToArray(),
                CurrentHash: metadata.Visibility == CommunityContentVisibility.Published
                    ? hash : current?.CurrentHash,
                LatestHash: hash,
                CreatedAt: current?.CreatedAt ?? now,
                UpdatedAt: now,
                Revisions: current == null
                    ? new[] { revision } : current.Revisions.Append(revision).ToArray(),
                Archived: current?.Archived ?? false);

            CommunityContentStoredProject[] updated = _projects.Values
                .Where(p => !(p.Kind == metadata.Kind && p.ContentId == metadata.ContentId))
                .Append(next)
                .OrderBy(p => p.Kind, StringComparer.Ordinal)
                .ThenBy(p => p.ContentId).ToArray();
            AtomicFile.Write(_index, JsonSerializer.SerializeToUtf8Bytes(
                new CommunityContentStoredState(Format, updated),
                MapPackageReader.JsonOptions));
            _projects[key] = next;
            return Project(next, owner: true)
                ?? throw new InvalidDataException("New v3 content could not be projected.");
        }
    }

    public CommunityContentProject[] ListVisible(string? viewerId)
    {
        lock (_gate)
            return _projects.Values
                .OrderBy(p => p.Kind, StringComparer.Ordinal)
                .ThenBy(p => p.ContentId)
                .Select(p => Project(p, viewerId == p.OwnerId))
                .OfType<CommunityContentProject>().ToArray();
    }

    public CommunityContentProject? GetVisible(string kind, Guid contentId, string? viewerId)
    {
        string normalized = CommunityContentKinds.Normalize(kind);
        if (normalized == CommunityContentKinds.Map) return null;
        lock (_gate)
            return _projects.TryGetValue((normalized, contentId), out var value)
                ? Project(value, viewerId == value.OwnerId) : null;
    }

    public CommunityContentRevision[] RevisionsVisible(
        string kind, Guid contentId, string? viewerId)
    {
        string normalized = CommunityContentKinds.Normalize(kind);
        if (normalized == CommunityContentKinds.Map) return Array.Empty<CommunityContentRevision>();
        lock (_gate)
        {
            if (!_projects.TryGetValue((normalized, contentId), out var value))
                return Array.Empty<CommunityContentRevision>();
            bool owner = value.OwnerId == viewerId;
            if (value.Archived && !owner) return Array.Empty<CommunityContentRevision>();
            return value.Revisions
                .Where(r => owner || r.Visibility == CommunityContentVisibility.Published)
                .OrderByDescending(r => r.RevisionNumber)
                .ToArray();
        }
    }

    public string? BlobPath(string contentHash)
    {
        if (!MapCommunityClient.ValidHash(contentHash)) return null;
        string path = Path.Combine(_objects, contentHash + ".blob");
        return File.Exists(path) ? path : null;
    }

    private void PutVerifiedBlob(string source, string hash, long bytes)
    {
        string output = Path.Combine(_objects, hash + ".blob");
        if (File.Exists(output))
        {
            if (new FileInfo(output).Length != bytes
                || MapBuildFingerprint.HashFile(output) != hash)
                throw new InvalidDataException(
                    "Existing immutable v3 content object is corrupted; operator repair required.");
            return;
        }
        string staging = Path.Combine(_objects,
            hash + "." + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            using (var input = new FileStream(source, FileMode.Open,
                FileAccess.Read, FileShare.Read, 65536, FileOptions.SequentialScan))
            using (var target = new FileStream(staging, FileMode.CreateNew,
                FileAccess.Write, FileShare.None, 65536, FileOptions.SequentialScan))
            {
                input.CopyTo(target, 65536);
                target.Flush(flushToDisk: true);
            }
            if (new FileInfo(staging).Length != bytes
                || MapBuildFingerprint.HashFile(staging) != hash)
                throw new InvalidDataException("V3 source changed during verified copy.");
            File.Move(staging, output);
        }
        finally
        {
            if (File.Exists(staging)) File.Delete(staging);
        }
    }

    private CommunityContentProject? Project(CommunityContentStoredProject data, bool owner)
    {
        if (data.Archived && !owner) return null;
        CommunityContentRevision[] visible = data.Revisions
            .Where(r => owner || r.Visibility == CommunityContentVisibility.Published)
            .ToArray();
        if (visible.Length == 0) return null;
        CommunityContentRevision latest = owner
            ? visible[^1] : visible[^1];
        CommunityContentRevision? current = data.CurrentHash != null
            ? visible.FirstOrDefault(r => r.ContentHash == data.CurrentHash)
            : null;
        if (!owner && current == null) return null;
        CommunityContentRevision selected = current ?? latest;
        // A missing referenced object is unavailable, not a downloadable
        // catalog entry. PR-09 will add exact verified download endpoints.
        if (BlobPath(selected.ContentHash) is not { } blob
            || new FileInfo(blob).Length != selected.Bytes) return null;
        return new CommunityContentProject(
            data.Kind, data.ContentId, data.Name, data.DisplayName, data.Author,
            owner ? data.OwnerId : null,
            data.Description, data.Tags.ToArray(),
            current?.ContentHash,
            latest.ContentHash,
            visible.Length,
            data.CreatedAt,
            data.UpdatedAt,
            current,
            latest,
            CanManage: owner, Archived: data.Archived);
    }

    private static void ValidateRegistration(CommunityContentRegistration m)
    {
        string normalized = CommunityContentKinds.Normalize(m.Kind);
        if (normalized == CommunityContentKinds.Map)
            throw new InvalidDataException("Map registrations must use the v2 map revision catalog.");
        if (m.Kind != normalized || m.ContentId == Guid.Empty
            || string.IsNullOrWhiteSpace(m.OwnerId) || m.OwnerId.Length > 128
            || !SafeText(m.Name, 128) || !SafeText(m.DisplayName, 128, optional: true)
            || !SafeText(m.Author, 128, optional: true)
            || !SafeText(m.Description, 2000, optional: true)
            || m.Tags == null || m.Tags.Length > MaximumTags
            || m.Tags.Any(t => !SafeText(t, 32))
            || m.Tags.Distinct(StringComparer.OrdinalIgnoreCase).Count() != m.Tags.Length
            || !SafeText(m.ReleaseNotes, 4000, optional: true)
            || m.ExpectedParentHash != null
                && !MapCommunityClient.ValidHash(m.ExpectedParentHash))
            throw new InvalidDataException("Invalid v3 creator metadata.");
        _ = CommunityContentVisibility.Normalize(m.Visibility);
        if (!SupportedMime(m.Kind, m.MimeType))
            throw new InvalidDataException("Unsupported v3 kind/MIME combination.");
        ValidateDependencies(m.Dependencies, m.Kind, m.ContentId);
    }

    private static void ValidateProject(CommunityContentStoredProject p)
    {
        if (p == null || p.Revisions == null || p.Revisions.Length == 0
            || p.Revisions.Length > MaximumRevisions
            || p.ContentId == Guid.Empty || !SafeText(p.OwnerId, 128)
            || !SafeText(p.Name, 128) || !SafeText(p.DisplayName, 128, true)
            || !SafeText(p.Author, 128, true) || !SafeText(p.Description, 2000, true)
            || p.Tags == null || p.Tags.Length > MaximumTags
            || p.Tags.Any(t => !SafeText(t, 32))
            || !MapCommunityClient.ValidHash(p.LatestHash)
            || p.CurrentHash != null && !MapCommunityClient.ValidHash(p.CurrentHash))
            throw new InvalidDataException("Invalid stored v3 content project.");
        string kind = CommunityContentKinds.Normalize(p.Kind);
        if (kind == CommunityContentKinds.Map || kind != p.Kind)
            throw new InvalidDataException("Map content cannot be duplicated in the v3 metadata store.");
        var hashes = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < p.Revisions.Length; i++)
        {
            var rev = p.Revisions[i];
            if (rev == null || rev.RevisionNumber != i + 1
                || !MapCommunityClient.ValidHash(rev.ContentHash)
                || rev.ArchiveHash != null || rev.Bytes is <= 0 or > MapPackageReader.MaxEntryBytes
                || !SupportedMime(kind, rev.MimeType)
                || !hashes.Add(rev.ContentHash)
                || rev.ParentHash != (i == 0 ? null : p.Revisions[i - 1].ContentHash)
                || !SafeText(rev.ReleaseNotes, 4000, true))
                throw new InvalidDataException("Invalid stored v3 content revision.");
            _ = CommunityContentVisibility.Normalize(rev.Visibility);
            ValidateDependencies(rev.Dependencies, kind, p.ContentId);
        }
        if (p.Revisions[^1].ContentHash != p.LatestHash
            || p.CurrentHash != null && !p.Revisions.Any(r =>
                r.ContentHash == p.CurrentHash
                && r.Visibility == CommunityContentVisibility.Published))
            throw new InvalidDataException("V3 content pointers differ from immutable revisions.");
    }

    private static bool SafeText(string? text, int maximum, bool optional = false)
        => optional && text == null || text is { Length: > 0 }
            && text.Length <= maximum && !string.IsNullOrWhiteSpace(text)
            && !text.Any(char.IsControl);

    private static bool SupportedMime(string kind, string mime) => kind switch
    {
        CommunityContentKinds.Texture =>
            mime is "image/png" or "image/jpeg" or "image/ktx2" or "image/x-tga",
        CommunityContentKinds.Material =>
            mime is "application/vnd.projectprime.material+json",
        CommunityContentKinds.Model =>
            mime is "model/gltf-binary" or "model/gltf+json",
        CommunityContentKinds.Prefab =>
            mime is "application/vnd.projectprime.prefab+json",
        _ => false
    };

    private static void ValidateDependencies(CommunityContentDependency[]? items,
        string kind, Guid id)
    {
        if (items == null) return;
        if (items.Length > MaximumDependencies) throw new InvalidDataException("Too many v3 dependencies.");
        foreach (CommunityContentDependency item in items)
        {
            if (item == null || item.ContentId == Guid.Empty
                || !MapCommunityClient.ValidHash(item.Hash)
                || (item.Kind == kind && item.ContentId == id))
                throw new InvalidDataException("Invalid or self-referencing v3 content dependency.");
            _ = CommunityContentKinds.Normalize(item.Kind);
        }
        if (items.Select(x => (x.Kind, x.ContentId))
            .Distinct().Count() != items.Length)
            throw new InvalidDataException("Duplicate v3 content dependency.");
    }
}

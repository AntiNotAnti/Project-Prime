using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace MphRead.Mods.MapGen;

public static class CommunityDependencyRoles
{
    public const string EditorOnly = "editor-only";
    public const string OptionalVisual = "optional-visual";
    public static bool IsAllowed(string? role)
        => role is EditorOnly or OptionalVisual;
}

public enum CommunityVisualQuality { Standard, Hd, Ultra }

public sealed record CommunityResolvedDependency(
    string Kind, Guid ContentId, string Hash, long Bytes, string Role);

/// <summary>
/// A published exact revision supplied by the authorized v3 revision API.
/// The lightweight project catalog includes only current/latest pointers;
/// historical revisions must be passed explicitly for older pinned hashes.
/// </summary>
public sealed record CommunityAuthorizedRevision(
    string Kind, Guid ContentId, CommunityContentRevision Revision);
public sealed record CommunityDependencyLockfile(
    int SchemaVersion, Guid MapId, string ExactMapPackageHash,
    CommunityVisualQuality Quality,
    CommunityResolvedDependency[] Items,
    CommunityContentDependency[] MissingOptional)
{
    public bool GameplayReadyUnaffected => true;
}

/// <summary>
/// Deterministic snapshot of optional, *visual/editor* dependencies. No map
/// runtime, collision, entities or multiplayer Ready path consumes this file.
/// Exact immutable identities are selected from already-authorized revisions;
/// mutable "latest" pointers are never followed after resolving.
/// </summary>
public static class CommunityDependencyResolver
{
    private const int MaximumDepth = 12;
    private const int MaximumItems = 128;
    private const int MaximumHistoryEntries = 2048;
    private const long MaximumBytes = 1024L * 1024 * 1024;

    public static CommunityDependencyLockfile Resolve(
        Guid mapId, string exactMapHash, CommunityVisualQuality quality,
        IEnumerable<CommunityContentDependency> roots,
        IEnumerable<CommunityContentProject> available,
        IEnumerable<CommunityAuthorizedRevision>? publishedHistory = null)
    {
        if (mapId == Guid.Empty || !MapCommunityClient.ValidHash(exactMapHash))
            throw new InvalidDataException("Optional profile needs an exact map SHA-256.");
        var lookup = available.ToLookup(x => (x.Kind, x.ContentId));
        var history = (publishedHistory ?? Array.Empty<CommunityAuthorizedRevision>())
            .Take(MaximumHistoryEntries + 1).ToArray();
        if (history.Length > MaximumHistoryEntries)
            throw new InvalidDataException("Optional revision history exceeds its input budget.");
        var older = new Dictionary<(string Kind, Guid Id, string Hash), CommunityContentRevision>();
        foreach (CommunityAuthorizedRevision entry in history)
        {
            if (entry == null || entry.ContentId == Guid.Empty
                || entry.Revision == null
                || !MapCommunityClient.ValidHash(entry.Revision.ContentHash)
                || entry.Revision.Visibility != CommunityContentVisibility.Published)
                throw new InvalidDataException("An optional historical revision is not publicly published.");
            string kind = CommunityContentKinds.Normalize(entry.Kind);
            if (kind == CommunityContentKinds.Map)
                throw new InvalidDataException("A visual revision cannot redirect a gameplay map.");
            var key = (kind, entry.ContentId, entry.Revision.ContentHash);
            if (!older.TryAdd(key, entry.Revision))
                throw new InvalidDataException("Duplicate immutable optional revision supplied.");
        }
        var resolved = new Dictionary<(string Kind, Guid Id, string Hash), CommunityResolvedDependency>();
        var visiting = new HashSet<(string Kind, Guid Id, string Hash)>();
        var missing = new Dictionary<(string Kind, Guid Id, string Hash), CommunityContentDependency>();
        long total = 0;

        void Walk(CommunityContentDependency dependency, int depth)
        {
            if (depth > MaximumDepth || resolved.Count + missing.Count >= MaximumItems)
                throw new InvalidDataException("Optional content dependency graph exceeds bounded limits.");
            if (!CommunityDependencyRoles.IsAllowed(dependency.Role)
                || !MapCommunityClient.ValidHash(dependency.Hash)
                || dependency.ContentId == Guid.Empty)
                throw new InvalidDataException("Invalid or gameplay-required content dependency.");
            string kind = CommunityContentKinds.Normalize(dependency.Kind);
            if (kind == CommunityContentKinds.Map)
                throw new InvalidDataException("Optional resource lockfiles cannot redirect gameplay maps.");
            var key = (kind, dependency.ContentId, dependency.Hash);
            if (resolved.ContainsKey(key)) return;
            if (!visiting.Add(key))
                throw new InvalidDataException("Cyclic optional resource dependency.");

            try
            {
                CommunityContentRevision? revision = null;
                if (older.TryGetValue(key, out var historical))
                    revision = historical;
                revision ??= lookup[(kind, dependency.ContentId)]
                    .SelectMany(p => p.CurrentRevision is null
                        ? new[] { p.LatestRevision }
                        : new[] { p.CurrentRevision, p.LatestRevision })
                    .FirstOrDefault(r => r.ContentHash == dependency.Hash
                        && r.Visibility == CommunityContentVisibility.Published);
                if (revision == null)
                {
                    missing[key] = dependency;
                    return;
                }
                foreach (CommunityContentDependency child in revision.Dependencies
                    ?? Array.Empty<CommunityContentDependency>())
                    Walk(child, depth + 1);

                if (revision.Bytes <= 0 || revision.Bytes > CommunityAssetValidator.MaxModelBytes
                    || revision.Bytes > MaximumBytes - total)
                    throw new InvalidDataException("Optional asset graph exceeds storage budget.");
                total += revision.Bytes;
                resolved.Add(key, new CommunityResolvedDependency(
                    kind, dependency.ContentId, dependency.Hash, revision.Bytes, dependency.Role));
            }
            finally { visiting.Remove(key); }
        }

        foreach (CommunityContentDependency root in roots) Walk(root, 0);
        static string Key(CommunityContentDependency x)
            => x.Kind + ":" + x.ContentId.ToString("D") + ":" + x.Hash;
        return new CommunityDependencyLockfile(1, mapId, exactMapHash, quality,
            resolved.Values.OrderBy(x => x.Kind, StringComparer.Ordinal)
                .ThenBy(x => x.ContentId).ThenBy(x => x.Hash, StringComparer.Ordinal)
                .ToArray(),
            missing.Values.OrderBy(Key, StringComparer.Ordinal).ToArray());
    }

    public static byte[] CanonicalBytes(CommunityDependencyLockfile file)
    {
        if (file.SchemaVersion != 1 || !MapCommunityClient.ValidHash(file.ExactMapPackageHash))
            throw new InvalidDataException("Unrecognized optional content lockfile.");
        return JsonSerializer.SerializeToUtf8Bytes(file, MapPackageReader.JsonOptions);
    }
}

/// <summary>
/// Separate immutable visual blob cache. It never installs a gameplay map.
/// Two different maps requesting an identical optional texture hash reuse a
/// single verified download and a single on-disk object.
/// </summary>
public sealed class CommunityOptionalAssetCache(string root)
{
    private readonly string _root = Path.GetFullPath(root);
    private readonly Dictionary<string, SemaphoreSlim> _locks = new(StringComparer.Ordinal);
    private readonly object _gate = new();

    public async Task<string> GetOrDownloadAsync(MapCommunityClient client,
        CommunityResolvedDependency dependency, CancellationToken cancellation)
    {
        ArgumentNullException.ThrowIfNull(client);
        if (!CommunityDependencyRoles.IsAllowed(dependency.Role)
            || !MapCommunityClient.ValidHash(dependency.Hash)
            || dependency.Bytes is <= 0 or > CommunityAssetValidator.MaxModelBytes)
            throw new InvalidDataException("Invalid optional content identity.");

        Directory.CreateDirectory(_root);
        SemaphoreSlim gate;
        lock (_gate)
        {
            if (!_locks.TryGetValue(dependency.Hash, out gate!))
                _locks.Add(dependency.Hash, gate = new SemaphoreSlim(1, 1));
        }
        await gate.WaitAsync(cancellation).ConfigureAwait(false);
        try
        {
            string path = Path.Combine(_root, dependency.Hash + ".blob");
            if (File.Exists(path) && new FileInfo(path).Length == dependency.Bytes
                && MapBuildFingerprint.HashFile(path) == dependency.Hash)
                return path;
            string partial = Path.Combine(_root, dependency.Hash + ".part");
            await client.DownloadContentAsync(dependency.Hash, partial,
                dependency.Bytes, cancellation).ConfigureAwait(false);
            if (MapBuildFingerprint.HashFile(partial) != dependency.Hash)
                throw new InvalidDataException("Optional resource failed exact SHA-256 validation.");
            File.Move(partial, path, overwrite: true);
            return path;
        }
        finally { gate.Release(); }
    }
}

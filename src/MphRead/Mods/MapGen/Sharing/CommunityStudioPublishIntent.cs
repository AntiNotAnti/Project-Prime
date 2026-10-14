using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace MphRead.Mods.MapGen;

public enum CommunityPublishVisibility { Published, Unlisted, Draft }

public sealed record CommunityStudioPublishReceipt(
    Guid JobId, Guid MapId, string RuntimeName, string PackageHash,
    bool IsRevision, string? ExpectedParentHash,
    CommunityPublishVisibility Visibility);

/// <summary>
/// Editor-independent creator handoff. Capture once while in an editor action:
/// no reference to MapDocument, mutable project state, UI control or credentials
/// can enter a durable upload job. Creator revisions use optimistic latest-hash
/// parenting and a stale parent is never silently overridden.
/// </summary>
public sealed class CommunityStudioPublishIntent
{
    private readonly MapBuildSnapshot _snapshot;
    public Guid MapId { get; }
    public string RuntimeName { get; }

    private CommunityStudioPublishIntent(MapDefinition source)
    {
        MapValidator.RequireRuntimeName(source.Name);
        if (source.MapId == Guid.Empty)
            throw new InvalidDataException("Creator publication requires a persistent MapId.");
        _snapshot = MapBuildSnapshot.Capture(source);
        MapId = source.MapId;
        RuntimeName = source.Name;
    }

    public static CommunityStudioPublishIntent Capture(MapDefinition source)
    {
        ArgumentNullException.ThrowIfNull(source);
        // The detached MapBuildSnapshot freezes the authoring graph. The
        // scheduler still fingerprints referenced assets before publishing.
        return new CommunityStudioPublishIntent(source);
    }

    public async Task<CommunityStudioPublishReceipt> QueueAsync(
        string endpoint, CommunityMapOperations operations,
        CommunityTransferManager transfers, CommunityPublishVisibility visibility,
        string? releaseNotes, CancellationToken cancellation = default)
    {
        ArgumentNullException.ThrowIfNull(operations);
        ArgumentNullException.ThrowIfNull(transfers);
        if (string.IsNullOrWhiteSpace(endpoint))
            throw new ArgumentException("A trusted Community endpoint is required.", nameof(endpoint));
        if (!Enum.IsDefined(visibility))
            throw new ArgumentOutOfRangeException(nameof(visibility));
        if (releaseNotes?.Length > 2000)
            throw new ArgumentOutOfRangeException(nameof(releaseNotes),
                "Release notes are limited to 2,000 characters.");
        cancellation.ThrowIfCancellationRequested();

        // Authenticate the owner's catalog before queueing. Never guess parent
        // hashes or accept a public map with the same name as proof of ownership.
        var owned = await operations.BrowseProjectsAsync(
            mine: true, favorites: false, sort: "updated", cancellation: cancellation)
            .ConfigureAwait(false);
        CommunityMapProject? existing = owned.FirstOrDefault(p => p.MapId == MapId);
        if (existing?.DeletedAt != null || existing?.ArchivedAt != null)
            throw new InvalidOperationException(
                "The current Community map is archived/deleted. Restore it explicitly before publishing.");
        if (existing != null && !existing.CanManageLifecycle)
            throw new InvalidOperationException("Creator permissions do not allow this revision.");

        var request = new CommunityPublishRequest(
            ExistingMap: existing != null,
            ExpectedParentHash: existing?.LatestHash,
            ReleaseNotes: releaseNotes,
            AllowStaleParent: false);
        var temp = Path.Combine(Path.GetTempPath(),
            "prime-community-intent-" + Guid.NewGuid().ToString("N") + ".ppmap");
        try
        {
            await MapBuildScheduler.Shared.PackageAsync(_snapshot, temp, cancellation)
                .ConfigureAwait(false);
            cancellation.ThrowIfCancellationRequested();
            string hash = await Task.Run(() =>
            {
                using var reader = new MapPackageReader(temp);
                if (reader.Manifest?.MapId != MapId
                    || !string.Equals(reader.Manifest.Name, RuntimeName, StringComparison.Ordinal))
                    throw new InvalidDataException(
                        "Frozen package no longer matches the selected creator MapId/name.");
                return MapBuildFingerprint.HashFile(temp);
            }, cancellation).ConfigureAwait(false);

            // EnqueueUploadAsync copies this archive into its owned durable
            // snapshot and verifies the copy. This temporary build file can
            // disappear on editor close without affecting upload/resume.
            Guid jobId = await transfers.EnqueueUploadAsync(
                endpoint, temp, request,
                listed: visibility == CommunityPublishVisibility.Published,
                draft: visibility == CommunityPublishVisibility.Draft,
                cancellation: cancellation).ConfigureAwait(false);
            return new CommunityStudioPublishReceipt(
                jobId, MapId, RuntimeName, hash, existing != null,
                existing?.LatestHash, visibility);
        }
        finally
        {
            if (File.Exists(temp)) File.Delete(temp);
        }
    }
}

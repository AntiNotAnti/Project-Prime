using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using MphRead.Mods.Launcher;
using MphRead.Mods.Network;

namespace MphRead.Mods.MapGen;

/// <summary>
/// Acquires immutable Community archives for all non-editor map consumers.
/// Compilation and publication remain exclusively owned by the caller.
/// </summary>
public static class CommunityExactArchiveAcquisition
{
    /// <summary>
    /// Caller owns the returned read lease. Game/Studio transfer jobs use the
    /// same CAS root and per-hash cross-process download gate.
    /// </summary>
    public static async Task<CommunityArchiveCache.ArchiveLease> AcquireAsync(
        MapContentIdentity required, string endpoint, CancellationToken cancellation,
        Action<string>? stage = null, Action<float>? progress = null,
        string? cacheRoot = null)
    {
        if (!required.IsCustom || required.MapId == Guid.Empty
            || required.ContentHash.IsZero || required.PackageHash.IsZero
            || string.IsNullOrWhiteSpace(required.RoomKey))
            throw new InvalidDataException("Invalid exact Community map identity.");
        cancellation.ThrowIfCancellationRequested();
        // Reject untrusted addresses even when the archive is already cached.
        MapCommunityClient.ValidateBaseAddress(endpoint);

        string root = cacheRoot ?? Path.Combine(LauncherPrefs.Directory, "community-transfers");
        var cache = new CommunityArchiveCache(root);
        string hash = required.PackageHash.ToString();

        stage?.Invoke("Verifying");
        CommunityArchiveCache.ArchiveLease? existing = cache.TryAcquire(required);
        if (existing != null) return existing;

        // A second process may be downloading this hash already. Wait for its
        // exclusive gate, then recheck the cache before any new HTTP request.
        using (await cache.AcquireDownloadGateAsync(hash, cancellation).ConfigureAwait(false))
        {
            existing = cache.TryAcquire(required);
            if (existing != null) return existing;

            string partialDir = Path.Combine(root, "partials");
            Directory.CreateDirectory(partialDir);
            string partial = Path.Combine(partialDir, hash + ".part");
            using var client = new MapCommunityClient(endpoint);
            CommunityMap? metadata = await client.GetPackageAsync(hash, cancellation)
                .ConfigureAwait(false);

            stage?.Invoke("Downloading");
            if (metadata == null)
            {
                // Legacy exact-hash route fallback when modern metadata does
                // not exist; it retains existing strict package validation.
                await client.DownloadExactAsync(required, partial, cancellation)
                    .ConfigureAwait(false);
                progress?.Invoke(1);
            }
            else
            {
                if (metadata.MapId != required.MapId
                    || metadata.Hash != hash
                    || metadata.ContentHash != required.ContentHash.ToString()
                    || !StringComparer.OrdinalIgnoreCase.Equals(metadata.Name, required.RoomKey)
                    || metadata.MinimumProtocol > NetConfig.ProtocolVersion)
                    throw new InvalidDataException("Community metadata disagrees with the required map identity.");

                await client.DownloadResumableAsync(metadata, partial, cancellation,
                    (received, total) => progress?.Invoke(total > 0
                        ? Math.Clamp((float)received / total, 0f, 1f) : 0f))
                    .ConfigureAwait(false);
            }

            cancellation.ThrowIfCancellationRequested();
            stage?.Invoke("Verifying");
            cache.Promote(partial, required);
            if (File.Exists(partial)) File.Delete(partial);
            existing = cache.TryAcquire(required)
                ?? throw new IOException("Verified Community archive could not be leased.");
        }
        return existing;
    }

    /// <summary>
    /// Prepare a validated detached build from a held CAS read lease. The
    /// owning network/session/UI thread still commits and marks Ready.
    /// </summary>
    public static async Task<PreparedMapInstallation> PrepareAsync(
        MapContentIdentity required, string endpoint, CancellationToken cancellation,
        Action<string>? stage = null, Action<float>? progress = null,
        string? cacheRoot = null)
    {
        var lease = await AcquireAsync(required, endpoint, cancellation,
            stage, progress, cacheRoot).ConfigureAwait(false);
        try
        {
            stage?.Invoke("Building");
            return await MapPackageInstaller.PrepareLeasedAsync(lease, cancellation)
                .ConfigureAwait(false);
        }
        catch
        {
            lease.Dispose();
            throw;
        }
    }
}

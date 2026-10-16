using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace MphRead.Mods.MapGen;

/// <summary>
/// Measured archive I/O for a prepared installation. Counts bytes written by
/// the installer, not network bytes, compiler output or operating-system caches.
/// No timing or platform-level disk metrics are inferred from these counters.
/// </summary>
public sealed record MapInstallationArchiveIo(
    long PreparationCopyBytes,
    long CommitCopyBytes,
    long BackupCopyBytes,
    bool ReusedExactArchive);

/// <summary>
/// A verified private archive and completed build. Publication remains an
/// explicit owner-thread operation, fenced by MapRuntimeUsage.
/// </summary>
public sealed class PreparedMapInstallation : IDisposable
{
    private readonly string _source;
    private readonly bool _ownsSource;
    private readonly MapBuildResult _build;
    private IDisposable? _sourceLease;
    private bool _disposed;

    public MapContentIdentity Identity { get; }
    public MapInstallationArchiveIo ArchiveIo { get; private set; }

    internal PreparedMapInstallation(string source, bool ownsSource,
        MapBuildResult build, MapContentIdentity identity, long preparationCopyBytes)
    {
        _source = source;
        _ownsSource = ownsSource;
        _build = build;
        Identity = identity;
        ArchiveIo = new(preparationCopyBytes, 0, 0, false);
    }

    /// <summary>
    /// Install the already-built exact map. A new destination is staged on the
    /// destination volume and renamed atomically. Existing map bytes are
    /// backed up only when a different package must replace them. The runtime
    /// install and rollback remain protected by the existing global scene gate.
    /// </summary>
    public MapDefinition Commit(string library, bool initialJoin = false)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(PreparedMapInstallation));
        lock (MapRuntimeUsage.Gate)
        {
            // Refuse before creating a library or any staging output, preserving
            // the active-scene and prewarm behavior of the original installer.
            MapRuntimeUsage.RequireInstallationAllowed(Identity.RoomKey, initialJoin);
            MapPackageInstaller.RequireNoConflict(Identity);
            if (!MapContentIdentity.FromPackage(_source).Matches(Identity))
                throw new InvalidDataException("Prepared map changed before installation.");

            Directory.CreateDirectory(library);
            string destination = Path.Combine(library, Identity.MapId.ToString("N") + MapBundle.Extension);
            string staged = destination + "." + Guid.NewGuid().ToString("N") + ".staging";
            string backup = destination + "." + Guid.NewGuid().ToString("N") + ".backup";

            // A matching installed archive needs neither another write nor a
            // rollback copy. This is a byte-level test, NOT identity-by-name.
            bool existing = File.Exists(destination);
            bool exactAlreadyInstalled = existing
                && MapPackageInstaller.IsExactArchive(destination, Identity);
            long copied = 0, backupBytes = 0;
            bool backupCreated = false, attemptedReplacement = false;
            try
            {
                if (!exactAlreadyInstalled)
                {
                    // Copy in bounded chunks, with no full-archive byte[].
                    // Verify the destination-volume staging bytes before the
                    // atomic replacement, so a changing source cannot slip in.
                    copied = MapPackageInstaller.StageExactArchive(_source, staged, Identity);
                    if (existing)
                    {
                        File.Copy(destination, backup);
                        backupBytes = new FileInfo(backup).Length;
                        backupCreated = true;
                    }
                    Mods.RoomPrewarm.Invalidate(Identity.RoomKey);
                    attemptedReplacement = true;
                    File.Move(staged, destination, overwrite: true);
                }
                else
                {
                    Mods.RoomPrewarm.Invalidate(Identity.RoomKey);
                }

                var installed = MapDefinition.Load(destination);
                MapBuildScheduler.Install(_build, installed, CustomRooms.ArchiveDirectory(installed),
                    CustomRooms.EntityDirectory(), CustomRooms.NodeDirectory());

                ArchiveIo = new(ArchiveIo.PreparationCopyBytes, copied, backupBytes,
                    exactAlreadyInstalled);
                return installed;
            }
            catch
            {
                // A failed preflight or stage copy never replaces an existing
                // package. After replacement, restore the original archive.
                // Runtime compilation/install retains its existing error policy.
                if (attemptedReplacement)
                {
                    if (backupCreated)
                        File.Move(backup, destination, overwrite: true);
                    else if (File.Exists(destination))
                        File.Delete(destination);
                }
                throw;
            }
            finally
            {
                if (File.Exists(staged)) File.Delete(staged);
                if (File.Exists(backup)) File.Delete(backup);
            }
        }
    }

    // The source cache lease must survive detached build and owning-thread
    // Commit. No other process may evict its immutable bytes in that window.
    internal void RetainSourceLease(IDisposable lease)
    {
        if (_sourceLease != null) throw new InvalidOperationException("Prepared map already owns a source lease.");
        _sourceLease = lease;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try
        {
            // Never delete a leased caller-owned cache archive on disposal.
            if (_ownsSource && File.Exists(_source)) File.Delete(_source);
        }
        finally { _sourceLease?.Dispose(); }
    }
}

public static class MapPackageInstaller
{
    internal static void RequireNoConflict(MapContentIdentity identity)
    {
        if (CustomRooms.Installed.TryGet(identity.MapId, out var previous)
            && !previous.Identity.RoomKey.Equals(identity.RoomKey, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("An installed map's runtime name is immutable. Use a new map identity when renaming it.");
        if (Metadata.IsBuiltInRoom(identity.RoomKey)) throw new InvalidDataException("A custom map cannot replace a built-in room.");
        var conflict = CustomRooms.Definitions.FirstOrDefault(d => d.Name.Equals(identity.RoomKey, StringComparison.OrdinalIgnoreCase)
            && (d.MapId == Guid.Empty ? MapPackageBuilder.LegacyId(d.Name) : d.MapId) != identity.MapId);
        if (conflict != null) throw new InvalidDataException("Another map already uses this runtime name. Rename the map before sharing.");
    }

    /// <summary>
    /// General mutable-source path. Preserve existing behavior by freezing
    /// caller-owned input into an isolated private snapshot before building.
    /// Other code may edit or delete the original after this method returns.
    /// </summary>
    public static async Task<PreparedMapInstallation> PrepareAsync(
        string download, MapContentIdentity required, CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        string snapshot = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".ppmap");
        try
        {
            long copied;
            await using (var source = new FileStream(download, FileMode.Open, FileAccess.Read,
                FileShare.Read, 65536, FileOptions.Asynchronous | FileOptions.SequentialScan))
            await using (var output = new FileStream(snapshot, FileMode.CreateNew, FileAccess.Write,
                FileShare.None, 65536, FileOptions.Asynchronous))
            {
                await MapCommunityClient.CopyBoundedAsync(source, output,
                    MapPackageReader.MaxArchiveBytes, token).ConfigureAwait(false);
                copied = output.Length;
                await output.FlushAsync(token).ConfigureAwait(false);
            }
            return await PrepareSourceAsync(snapshot, required, ownsSource: true, copied,
                token).ConfigureAwait(false);
        }
        catch
        {
            if (File.Exists(snapshot)) File.Delete(snapshot);
            throw;
        }
    }

    /// <summary>
    /// Private completed download whose temporary file is transferred to the
    /// prepared installation. No redundant pre-build archive snapshot. On
    /// failure the private download is deleted; on success Dispose owns it.
    /// Not for paths owned by callers, installed libraries or cache objects.
    /// </summary>
    internal static Task<PreparedMapInstallation> PrepareOwnedAsync(
        string privateDownload, MapContentIdentity required, CancellationToken token = default,
        Action? onValidated = null)
        => PrepareSourceAsync(privateDownload, required, ownsSource: true,
            preparationCopiedBytes: 0, token, onValidated);

    /// <summary>
    /// A CAS read lease protects a verified archive across validation, build
    /// and commit. This synchronous wrapper owns the lease lifetime and does
    /// not copy the source or permit MapPackageInstaller to delete it.
    /// </summary>
    /// <summary>
    /// Use verified CAS bytes without copying them again. Ownership of the
    /// lease passes to the prepared installation until it is disposed.
    /// </summary>
    internal static async Task<PreparedMapInstallation> PrepareLeasedAsync(
        CommunityArchiveCache.ArchiveLease lease, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(lease);
        try
        {
            var prepared = await PrepareSourceAsync(lease.Path, lease.Identity,
                ownsSource: false, preparationCopiedBytes: 0, token).ConfigureAwait(false);
            prepared.RetainSourceLease(lease);
            return prepared;
        }
        catch
        {
            lease.Dispose();
            throw;
        }
    }

    public static MapDefinition InstallLeased(CommunityArchiveCache.ArchiveLease lease,
        Guid id, string contentHash, string archiveHash, string library)
    {
        ArgumentNullException.ThrowIfNull(lease);
        using (lease)
        {
            MapContentIdentity required = lease.Identity;
            if (required.MapId != id || required.ContentHash != MapHash256.Parse(contentHash)
                || required.PackageHash != MapHash256.Parse(archiveHash))
                throw new InvalidDataException("Leased map identity does not match the selected map.");
            using var prepared = PrepareSourceAsync(lease.Path, required,
                ownsSource: false, preparationCopiedBytes: 0, CancellationToken.None)
                .GetAwaiter().GetResult();
            return prepared.Commit(library);
        }
    }

    private static async Task<PreparedMapInstallation> PrepareSourceAsync(
        string source, MapContentIdentity required, bool ownsSource,
        long preparationCopiedBytes, CancellationToken token,
        Action? onValidated = null)
    {
        try
        {
            token.ThrowIfCancellationRequested();
            var identity = MapContentIdentity.FromPackage(source);
            if (!identity.Matches(required))
                throw new InvalidDataException("Downloaded map identity or package hash does not match the server.");
            RequireNoConflict(identity);
            var definition = MapDefinition.Load(source);
            onValidated?.Invoke();
            var build = await MapBuildScheduler.Shared.BuildAsync(
                MapBuildSnapshot.Capture(definition), token).ConfigureAwait(false);
            MapCompiler.ThrowIfInvalid(build.Validation());
            token.ThrowIfCancellationRequested();
            return new PreparedMapInstallation(source, ownsSource, build, identity,
                preparationCopiedBytes);
        }
        catch
        {
            if (ownsSource && File.Exists(source)) File.Delete(source);
            throw;
        }
    }

    /// <summary>
    /// The existing library is byte-identical only if its full archive digest
    /// equals the already strictly validated package hash.
    /// </summary>
    internal static bool IsExactArchive(string path, MapContentIdentity identity)
    {
        try
        {
            return File.Exists(path) && MapHash256.HashFile(path) == identity.PackageHash;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>
    /// The temporary resides alongside the destination, so the final rename
    /// is atomic on that volume. One 64 KiB copy buffer replaces a giant
    /// File.ReadAllBytes + WriteAllBytes allocation.
    /// </summary>
    internal static long StageExactArchive(string source, string staged,
        MapContentIdentity required)
    {
        long bytes;
        using (var input = new FileStream(source, FileMode.Open, FileAccess.Read,
            FileShare.Read, 65536, FileOptions.SequentialScan))
        using (var output = new FileStream(staged, FileMode.CreateNew, FileAccess.Write,
            FileShare.None, 65536, FileOptions.SequentialScan))
        {
            bytes = input.Length;
            if (bytes is <= 0 or > MapPackageReader.MaxArchiveBytes)
                throw new InvalidDataException("Map archive exceeds package size limit.");
            input.CopyTo(output, 65536);
            output.Flush();
            if (output.Length != bytes)
                throw new InvalidDataException("Staged map archive length changed during publication.");
        }
        if (MapHash256.HashFile(staged) != required.PackageHash)
            throw new InvalidDataException("Staged map archive bytes changed during copy.");
        return bytes;
    }

    public static MapDefinition Install(string download, Guid id, string contentHash,
        string archiveHash, string library)
    {
        var identity = MapContentIdentity.FromPackage(download);
        if (identity.MapId != id || identity.ContentHash != MapHash256.Parse(contentHash)
            || identity.PackageHash != MapHash256.Parse(archiveHash))
            throw new InvalidDataException("Downloaded map identity does not match the server.");
        using var prepared = PrepareAsync(download, identity).GetAwaiter().GetResult();
        return prepared.Commit(library);
    }
}

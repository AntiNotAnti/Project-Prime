using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace MphRead.Mods.MapGen;

/// <summary>
/// Bounded immutable .ppmap archive store. Hash-based names never come from
/// author-chosen paths. Every opened archive is strictly verified against the
/// full MapId/room/content/package identity before being leased.
/// </summary>
public sealed class CommunityArchiveCache
{
    private const long DefaultMaxBytes = 4L * 1024 * 1024 * 1024;
    private readonly string _root;
    private readonly string _archives;
    private readonly string _locks;
    private readonly string _access;
    private readonly string _quarantine;

    public long MaximumBytes { get; }

    public CommunityArchiveCache(string root, long maximumBytes = DefaultMaxBytes)
    {
        if (maximumBytes is < 1 or > 1024L * 1024 * 1024 * 1024)
            throw new ArgumentOutOfRangeException(nameof(maximumBytes));
        _root = Path.GetFullPath(root);
        _archives = Path.Combine(_root, "archives");
        _locks = Path.Combine(_root, "locks");
        _access = Path.Combine(_root, "access");
        _quarantine = Path.Combine(_root, "quarantine");
        MaximumBytes = maximumBytes;
        foreach (string directory in new[] { _archives, _locks, _access, _quarantine })
            Directory.CreateDirectory(directory);
    }

    /// <summary>
    /// A shared cross-process file lease. Dispose it only after consumers have
    /// finished reading, preparing or installing the immutable archive.
    /// </summary>
    public sealed class ArchiveLease : IDisposable
    {
        private readonly FileStream _gate;
        public string Path { get; }
        public MapContentIdentity Identity { get; }
        internal ArchiveLease(FileStream gate, string path, MapContentIdentity identity)
        {
            _gate = gate;
            Path = path;
            Identity = identity;
        }
        public void Dispose() => _gate.Dispose();
    }

    /// <summary>
    /// A process-wide download gate for one exact hash. Holds a separate lock
    /// while HTTP Range work is in progress, preventing multiple processes from
    /// simultaneously writing the same partial archive.
    /// </summary>
    public sealed class DownloadGate : IDisposable
    {
        private readonly FileStream _gate;
        internal DownloadGate(FileStream gate) => _gate = gate;
        public void Dispose() => _gate.Dispose();
    }

    public async Task<DownloadGate> AcquireDownloadGateAsync(string packageHash,
        CancellationToken cancellation = default)
    {
        ValidateHash(packageHash);
        string path = Path.Combine(_locks, packageHash + ".download");
        while (true)
        {
            cancellation.ThrowIfCancellationRequested();
            try
            {
                return new DownloadGate(new FileStream(path, FileMode.OpenOrCreate,
                    FileAccess.ReadWrite, FileShare.None, bufferSize: 1));
            }
            catch (IOException)
            {
                // Another Game/Studio process owns this hash; wait rather than
                // issue competing HTTP requests or append to the same .part file.
                await Task.Delay(100, cancellation).ConfigureAwait(false);
            }
        }
    }

    public ArchiveLease? TryAcquire(MapContentIdentity required)
    {
        RequireIdentity(required);
        string hash = required.PackageHash.ToString();
        FileStream? gate = OpenShared(hash);
        if (gate == null) return null;
        try
        {
            string path = ArchivePath(hash);
            if (!File.Exists(path)) return null;
            if (File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint)) return null;
            MapContentIdentity found = MapContentIdentity.FromPackage(path);
            if (!found.Matches(required)) return null;
            Touch(hash);
            FileStream owned = gate;
            gate = null;
            return new ArchiveLease(owned, path, found);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException
            or UnauthorizedAccessException)
        {
            // Corrupted cache data is never treated as trusted. A subsequent
            // exclusive promotion can quarantine it and fetch the true bytes.
            return null;
        }
        finally { gate?.Dispose(); }
    }

    /// <summary>
    /// Atomically publishes a verified caller-owned staging file into the cache.
    /// An existing valid object is never overwritten, and damaged cache bytes
    /// are moved aside under an exclusive cross-process lease.
    /// </summary>
    public void Promote(string stagedPath, MapContentIdentity required)
    {
        RequireIdentity(required);
        string hash = required.PackageHash.ToString();
        if (!MapContentIdentity.FromPackage(stagedPath).Matches(required))
            throw new InvalidDataException("Staging archive did not match exact Community identity.");
        string destination = ArchivePath(hash);
        using FileStream gate = OpenExclusive(hash)
            ?? throw new IOException("Archive is leased by another process; retry promotion.");
        if (File.Exists(destination))
        {
            if (!File.GetAttributes(destination).HasFlag(FileAttributes.ReparsePoint))
            {
                try
                {
                    if (MapContentIdentity.FromPackage(destination).Matches(required))
                    {
                        Touch(hash);
                        return;
                    }
                }
                catch (Exception ex) when (ex is InvalidDataException or IOException
                    or UnauthorizedAccessException) { }
            }
            string orphan = Path.Combine(_quarantine,
                hash + "." + Guid.NewGuid().ToString("N") + ".corrupt");
            File.Move(destination, orphan);
        }

        // The staging source can be on another volume. Copy into the cache's
        // own staging directory and make the final rename atomic.
        string temporary = destination + "." + Guid.NewGuid().ToString("N") + ".staging";
        try
        {
            using (var source = File.OpenRead(stagedPath))
            using (var output = new FileStream(temporary, FileMode.CreateNew,
                FileAccess.Write, FileShare.None, 65536, FileOptions.SequentialScan))
                source.CopyTo(output, 65536);
            if (!MapContentIdentity.FromPackage(temporary).Matches(required))
                throw new InvalidDataException("Copied archive failed immutable integrity verification.");
            File.Move(temporary, destination);
            Touch(hash);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    /// <summary>
    /// Evict least-recently-used, unleased archive objects until under the
    /// configured quota. Never deletes installed packages or active readers.
    /// Deletion is skipped when another process holds a read or download lease.
    /// </summary>
    public (long BeforeBytes, long AfterBytes, int Deleted) Prune()
    {
        var objects = Directory.EnumerateFiles(_archives, "*.ppmap")
            .Select(path => (Path: path, Hash: Path.GetFileNameWithoutExtension(path)))
            .Where(item => MapCommunityClient.ValidHash(item.Hash))
            .Select(item => (item.Path, item.Hash,
                Bytes: new FileInfo(item.Path).Length,
                Access: LastAccess(item.Hash)))
            .OrderBy(item => item.Access).ThenBy(item => item.Hash, StringComparer.Ordinal)
            .ToArray();
        long total = objects.Sum(item => item.Bytes);
        long before = total;
        int deleted = 0;
        foreach (var item in objects)
        {
            if (total <= MaximumBytes) break;
            using FileStream? gate = OpenExclusive(item.Hash);
            if (gate == null) continue;
            if (!File.Exists(item.Path)) continue;
            if (File.GetAttributes(item.Path).HasFlag(FileAttributes.ReparsePoint)) continue;
            long bytes = new FileInfo(item.Path).Length;
            try
            {
                File.Delete(item.Path);
                string touch = TouchPath(item.Hash);
                if (File.Exists(touch)) File.Delete(touch);
                total -= bytes;
                deleted++;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Other processes and OS permissions can prevent eviction; fail
                // to free the object rather than violating archive correctness.
            }
        }
        return (before, total, deleted);
    }

    private FileStream? OpenShared(string hash)
    {
        try
        {
            return new FileStream(Path.Combine(_locks, hash + ".lease"),
                FileMode.OpenOrCreate, FileAccess.Read, FileShare.Read, bufferSize: 1);
        }
        catch (IOException) { return null; }
    }

    private FileStream? OpenExclusive(string hash)
    {
        try
        {
            return new FileStream(Path.Combine(_locks, hash + ".lease"),
                FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None, bufferSize: 1);
        }
        catch (IOException) { return null; }
    }

    private static void ValidateHash(string hash)
    {
        if (!MapCommunityClient.ValidHash(hash))
            throw new InvalidDataException("Invalid Community archive hash.");
    }
    private static void RequireIdentity(MapContentIdentity required)
    {
        if (!required.IsCustom || required.MapId == Guid.Empty
            || required.ContentHash.IsZero || required.PackageHash.IsZero
            || string.IsNullOrWhiteSpace(required.RoomKey))
            throw new InvalidDataException("Invalid exact Community map identity.");
    }
    private string ArchivePath(string hash) => Path.Combine(_archives, hash + ".ppmap");
    private string TouchPath(string hash) => Path.Combine(_access, hash + ".touch");
    private DateTime LastAccess(string hash)
    {
        string path = TouchPath(hash);
        return File.Exists(path) ? File.GetLastWriteTimeUtc(path) : DateTime.MinValue;
    }
    private void Touch(string hash)
    {
        try
        {
            string path = TouchPath(hash);
            using (File.Open(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.ReadWrite)) { }
            File.SetLastWriteTimeUtc(path, DateTime.UtcNow);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Access metadata is advisory, never a prerequisite for a verified
            // gameplay archive.
        }
    }
}

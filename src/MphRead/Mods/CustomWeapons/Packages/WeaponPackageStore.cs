using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;

namespace MphRead.Mods.CustomWeapons.Packages;

public sealed class WeaponPackageStoreOptions
{
    public int MaximumArchives { get; init; } = 256;
    public int MaximumInstalled { get; init; } = 128;
    public long MaximumTotalArchiveBytes { get; init; } = 512L * 1024 * 1024;
    public TimeSpan LockTimeout { get; init; } = TimeSpan.FromSeconds(10);
    internal Action<WeaponPackageStoreCheckpoint>? Checkpoint { get; init; }
}

internal enum WeaponPackageStoreCheckpoint { ArchiveStaged, ArchivePublished, MarkerStaged, BeforeMarkerPromotion, MarkerPublished }

public sealed record WeaponPackageStoreEntry(string PackageHash, string ContentHash, string DependencyHash, Guid ContentId, string Name);

/// <summary>Retains immutable validated bytes and an OS shared lease until its owner releases it.</summary>
public sealed class WeaponPackageLease : IDisposable
{
    private FileStream? _stream;
    public WeaponPackage Package { get; }
    internal WeaponPackageLease(FileStream stream, WeaponPackage package) { _stream = stream; Package = package; }
    public void Dispose() => Interlocked.Exchange(ref _stream, null)?.Dispose();
}

/// <summary>
/// Private local store; no registry, inventory or scene side effects. All cooperating
/// processes use the persistent gate before acquiring shared archive handles or pruning.
/// The store directory must be writable only by its user (not a hostile shared folder).
/// </summary>
public sealed class WeaponPackageStore
{
    private readonly string _root, _archives, _installed, _gate;
    private readonly WeaponPackageStoreOptions _options;
    private const int MaximumDirectoryEntries = 4096;
    private const string MarkerSuffix = ".installed.json";

    public WeaponPackageStore(string root, WeaponPackageStoreOptions? options = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        SafePath(root);
        _root = Path.GetFullPath(root); _archives = Path.Combine(_root, "archives");
        _installed = Path.Combine(_root, "installed"); _gate = Path.Combine(_root, ".store.lock");
        _options = options ?? new();
        if (_options.MaximumArchives is < 1 or > 1024 || _options.MaximumInstalled is < 1 or > 1024
            || _options.MaximumTotalArchiveBytes is < 1 or > 8L * 1024 * 1024 * 1024
            || _options.LockTimeout <= TimeSpan.Zero || _options.LockTimeout > TimeSpan.FromMinutes(1))
            throw new ArgumentOutOfRangeException(nameof(options));
        SafePath(_root); // Construction never creates state.
    }

    public WeaponPackage Install(ReadOnlyMemory<byte> archive, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var package = WeaponPackage.Parse(archive); // Invalid input cannot create even the gate/root.
        byte[] bytes = package.CopyArchive();
        cancellationToken.ThrowIfCancellationRequested();
        using var gate = Enter(true, cancellationToken)!;
        var archives = ArchiveFiles(); var markers = MarkerFiles();
        string target = ArchivePath(package.PackageHash), marker = MarkerPath(package.PackageHash);
        bool exists = File.Exists(target);
        if (exists)
        {
            using var retained = ReadLease(package.PackageHash);
            if (!retained.Package.CopyArchive().AsSpan().SequenceEqual(bytes)) throw Bad("Archive revision collision/corruption.");
        }
        if (File.Exists(marker))
        {
            VerifyMarker(marker, package);
            if (!exists) throw Bad("Installed archive is missing.");
            return package;
        }
        if (markers.Count >= _options.MaximumInstalled) throw Bad("Installed package quota; uninstall explicitly.");
        long total = 0;
        foreach (string path in Entries(_archives))
        {
            if (IsArchive(Path.GetFileName(path)) || IsStage(Path.GetFileName(path)))
            { SafeFile(path); total = checked(total + new FileInfo(path).Length); }
        }
        if (!exists && (archives.Count >= _options.MaximumArchives || bytes.Length > _options.MaximumTotalArchiveBytes - total))
            throw Bad("Archive quota; prune explicitly.");

        string? archiveStage = null, markerStage = null;
        try
        {
            if (!exists)
            {
                archiveStage = Stage(_archives, bytes);
                Check(WeaponPackageStoreCheckpoint.ArchiveStaged, cancellationToken);
                SafePath(target); File.Move(archiveStage, target, false); archiveStage = null;
                Check(WeaponPackageStoreCheckpoint.ArchivePublished, cancellationToken);
            }
            markerStage = Stage(_installed, Marker(package));
            Check(WeaponPackageStoreCheckpoint.MarkerStaged, cancellationToken);
            Check(WeaponPackageStoreCheckpoint.BeforeMarkerPromotion, cancellationToken);
            SafePath(marker); File.Move(markerStage, marker, false); markerStage = null;
            // Final commit point. There is deliberately no cancellation, fallible work,
            // or rollback after publication. Test-only observation cannot undo a commit.
            try { _options.Checkpoint?.Invoke(WeaponPackageStoreCheckpoint.MarkerPublished); } catch { }
            return package;
        }
        finally { DeleteOwnedStage(archiveStage); DeleteOwnedStage(markerStage); }
    }

    public IReadOnlyList<WeaponPackageStoreEntry> List()
    {
        using var gate = Enter(false, default);
        if (gate == null) return Array.Empty<WeaponPackageStoreEntry>();
        var result = new List<WeaponPackageStoreEntry>();
        foreach (string marker in MarkerFiles())
        {
            string hash = Path.GetFileName(marker)[..^MarkerSuffix.Length];
            using var lease = ReadLease(hash); VerifyMarker(marker, lease.Package);
            var p = lease.Package; result.Add(new(p.PackageHash, p.ContentHash, p.DependencyHash, p.Project.ContentId, p.Project.Name));
        }
        return result.AsReadOnly();
    }

    public WeaponPackageLease Acquire(string packageHash)
    {
        Hash(packageHash);
        using var gate = Enter(false, default);
        if (gate == null) throw new FileNotFoundException("Weapon package archive is unavailable.");
        return ReadLease(packageHash); // Open under gate, retain after gate is released.
    }

    public bool Uninstall(string packageHash)
    {
        Hash(packageHash);
        using var gate = Enter(false, default);
        if (gate == null) return false;
        string marker = MarkerPath(packageHash); SafePath(marker);
        if (!File.Exists(marker)) return false;
        using (var lease = ReadLease(packageHash)) VerifyMarker(marker, lease.Package);
        File.Delete(marker); // Archives and existing leases remain valid.
        return true;
    }

    public int Prune(CancellationToken cancellationToken = default)
    {
        using var gate = Enter(false, cancellationToken);
        if (gate == null) return 0;
        // Validate installed references first; corrupt markers never permit archive deletion.
        var pinned = new HashSet<string>(StringComparer.Ordinal);
        foreach (string marker in MarkerFiles())
        {
            string hash = Path.GetFileName(marker)[..^MarkerSuffix.Length];
            using var lease = ReadLease(hash); VerifyMarker(marker, lease.Package); pinned.Add(hash);
        }
        int removed = 0;
        foreach (string path in ArchiveFiles())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (pinned.Contains(Path.GetFileNameWithoutExtension(path))) continue;
            FileStream exclusive;
            try { exclusive = new(path, FileMode.Open, FileAccess.Read, FileShare.None); }
            catch (IOException) { continue; } // Real OS leases, including other processes.
            exclusive.Dispose(); // Gate excludes new leases between this close and unlink.
            File.Delete(path); removed++;
        }
        foreach (string directory in new[] { _archives, _installed })
            foreach (string path in Entries(directory))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (IsStage(Path.GetFileName(path))) { SafeFile(path); File.Delete(path); }
            }
        return removed;
    }

    private FileStream? Enter(bool create, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested(); SafePath(_root);
        if (!Directory.Exists(_root) && !create) return null;
        if (create) Directory.CreateDirectory(_root);
        SafePath(_gate);
        var watch = Stopwatch.StartNew();
        FileStream gate;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested(); SafePath(_gate);
            try { gate = new(_gate, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); break; }
            catch (IOException) when (watch.Elapsed < _options.LockTimeout)
            { if (cancellationToken.WaitHandle.WaitOne(20)) cancellationToken.ThrowIfCancellationRequested(); }
        }
        try
        {
            SafeFile(_gate);
            foreach (string directory in new[] { _archives, _installed })
            {
                SafePath(directory);
                if (create) Directory.CreateDirectory(directory);
                if (File.Exists(directory)) throw Bad("Store subdirectory is a file.");
            }
            return gate;
        }
        catch { gate.Dispose(); throw; }
        // Never unlink the gate: doing so would create independent lock domains.
    }

    // Enumeration has a fixed safety ceiling; lowering admission quotas must
    // never prevent listing, uninstalling or pruning an existing valid store.
    private List<string> ArchiveFiles() => Recognized(_archives, IsArchive, 1024);
    private List<string> MarkerFiles() => Recognized(_installed, name => name.EndsWith(MarkerSuffix, StringComparison.Ordinal)
        && ValidHash(name[..^MarkerSuffix.Length]), 1024);
    private static List<string> Recognized(string directory, Func<string, bool> recognizes, int maximum)
    {
        var files = new List<string>();
        foreach (string path in Entries(directory))
            if (recognizes(Path.GetFileName(path))) { SafeFile(path); files.Add(path); if (files.Count > maximum) throw Bad("Store entry quota."); }
        files.Sort(StringComparer.Ordinal); return files;
    }
    private static IEnumerable<string> Entries(string directory)
    {
        SafePath(directory);
        if (!Directory.Exists(directory)) yield break;
        int count = 0;
        foreach (string path in Directory.EnumerateFileSystemEntries(directory))
        { if (++count > MaximumDirectoryEntries) throw Bad("Store directory entry budget."); yield return path; }
    }
    private WeaponPackageLease ReadLease(string hash)
    {
        string path = ArchivePath(hash); SafeFile(path);
        var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        try
        {
            if (stream.Length is <= 0 or > WeaponPackageLimits.MaximumArchiveBytes) throw Bad("Stored archive size.");
            byte[] bytes = new byte[(int)stream.Length]; stream.ReadExactly(bytes);
            var package = WeaponPackage.Parse(bytes);
            if (package.PackageHash != hash) throw Bad("Stored archive identity mismatch.");
            return new(stream, package);
        }
        catch { stream.Dispose(); throw; }
    }
    private string ArchivePath(string hash) => Path.Combine(_archives, hash + ".ppweapon");
    private string MarkerPath(string hash) => Path.Combine(_installed, hash + MarkerSuffix);
    private static byte[] Marker(WeaponPackage p) => WeaponPackageJson.Write(w =>
    {
        w.WriteStartObject(); w.WriteNumber("schema", 1); w.WriteString("packageHash", p.PackageHash);
        w.WriteString("contentHash", p.ContentHash); w.WriteString("dependencyHash", p.DependencyHash);
        w.WriteString("contentId", p.Project.ContentId.ToString("D")); w.WriteEndObject();
    });
    private static void VerifyMarker(string path, WeaponPackage p)
    {
        SafeFile(path);
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        byte[] expected = Marker(p);
        if (stream.Length != expected.Length) throw Bad("Installed marker size/corruption.");
        byte[] bytes = new byte[expected.Length]; stream.ReadExactly(bytes);
        if (!bytes.AsSpan().SequenceEqual(expected)) throw Bad("Installed marker identity/corruption.");
    }
    private static string Stage(string directory, byte[] bytes)
    {
        string path = Path.Combine(directory, ".cw11b-" + Guid.NewGuid().ToString("N") + ".tmp");
        bool owned = false;
        try
        {
            using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            owned = true;
            stream.Write(bytes); stream.Flush(true); return path;
        }
        catch { if (owned) DeleteOwnedStage(path); throw; }
    }
    private static void DeleteOwnedStage(string? path)
    {
        if (path == null) return;
        // Cleanup only a staging name generated and retained by this operation.
        try { SafeFile(path); File.Delete(path); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }
    private void Check(WeaponPackageStoreCheckpoint point, CancellationToken cancellationToken)
    { _options.Checkpoint?.Invoke(point); cancellationToken.ThrowIfCancellationRequested(); }
    private static bool IsArchive(string name) => name.EndsWith(".ppweapon", StringComparison.Ordinal) && ValidHash(name[..^9]);
    private static bool IsStage(string name) => name.Length == 43 && name.StartsWith(".cw11b-", StringComparison.Ordinal)
        && name.EndsWith(".tmp", StringComparison.Ordinal) && name.AsSpan(7, 32).IndexOfAnyExcept("0123456789abcdef") < 0;
    private static bool ValidHash(string? hash) => hash?.Length == 64 && hash.AsSpan().IndexOfAnyExcept("0123456789abcdef") < 0;
    private static void Hash(string hash) { if (!ValidHash(hash)) throw new ArgumentException("Expected lowercase SHA-256 package hash.", nameof(hash)); }
    internal static void SafePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || path.Length > 4096 || path.Any(char.IsControl)) throw Bad("Invalid store path.");
        foreach (string part in path.Split('/', '\\'))
            if (part is not ("." or "..") && (part.EndsWith(' ') || part.EndsWith('.')))
                throw Bad("Ambiguous store path.");
        string full = Path.GetFullPath(path);
        if (full.StartsWith(@"\\", StringComparison.Ordinal)) throw Bad("Network/device store paths are unsupported.");
        string volume = Path.GetPathRoot(full)!;
        foreach (string part in full[volume.Length..].Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
        {
            if (part.Contains(':') || part.EndsWith(' ') || part.EndsWith('.')) throw Bad("Unsafe store path.");
            string stem = part.Split('.')[0].ToLowerInvariant();
            if (stem is "con" or "prn" or "aux" or "nul"
                || stem.Length == 4 && (stem.StartsWith("com", StringComparison.Ordinal) || stem.StartsWith("lpt", StringComparison.Ordinal))
                    && stem[3] is >= '0' and <= '9') throw Bad("Device store path.");
        }
        // Check every existing ancestor, including dangling links (Exists alone misses those).
        for (string? current = full; current != null; current = Path.GetDirectoryName(current))
        {
            FileAttributes attributes;
            try { attributes = File.GetAttributes(current); }
            catch (FileNotFoundException) { continue; } catch (DirectoryNotFoundException) { continue; }
            if ((attributes & FileAttributes.ReparsePoint) != 0) throw Bad("Links/reparse points are unsupported in store paths.");
        }
    }
    private static void SafeFile(string path)
    {
        SafePath(path);
        if (File.Exists(path) && (File.GetAttributes(path) & (FileAttributes.Directory | FileAttributes.Device)) != 0
            || Directory.Exists(path)) throw Bad("Expected regular store file.");
    }
    private static InvalidDataException Bad(string message) => WeaponPackageJson.Bad(message);
}

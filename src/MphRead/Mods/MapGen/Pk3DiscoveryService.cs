using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Diagnostics;
using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;

namespace MphRead.Mods.MapGen;

/// <summary>Local archive discovery and immutable, bounded Q3 conversion artifacts.</summary>
public static class Pk3DiscoveryService
{
    public const string ConverterVersion = "prime-q3-3";
    public const long MaxArchiveBytes = 512L * 1024 * 1024;
    public const long MaxExpandedBytes = 1024L * 1024 * 1024;
    public const long MaxEntryBytes = 128L * 1024 * 1024;
    public const int MaxEntries = 20000;
    private static readonly TimeSpan CacheLeaseTimeout = TimeSpan.FromSeconds(5);
    private const string CacheLeaseFileName = ".publication.lock";
    private static readonly object CacheLock = new();
    private static readonly StringComparer StringComparerForPaths = OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
    private static readonly string DefaultCacheRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ProjectPrime", "q3-conversion-cache");
    internal static string? CacheDirectoryOverride { get; set; }
    private static string CacheRoot
    {
        get
        {
            string? configured = CacheDirectoryOverride;
            if (String.IsNullOrWhiteSpace(configured))
                configured = Environment.GetEnvironmentVariable("PROJECT_PRIME_Q3_CACHE");
            return Path.GetFullPath(String.IsNullOrWhiteSpace(configured) ? DefaultCacheRoot : configured);
        }
    }
    public sealed record Level(string Name, string Entry, long Bytes);

    public static IReadOnlyList<Level> Discover(string source)
    {
        using var archive = OpenValidated(source);
        var levels = archive.Entries.Where(e => e.FullName.EndsWith(".bsp", StringComparison.OrdinalIgnoreCase))
            .Select(e => new Level(Path.GetFileNameWithoutExtension(e.FullName), e.FullName, e.Length))
            .OrderBy(e => e.Name, StringComparer.Ordinal).ToArray();
        if (levels.Select(e => e.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count() != levels.Length)
            throw new InvalidDataException("Archive contains ambiguous BSP level names.");
        return levels;
    }

    internal static ZipArchive OpenValidated(string source)
    {
        var stream = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read);
        ZipArchive archive;
        try
        {
            if (stream.Length > MaxArchiveBytes)
                throw new InvalidDataException("Quake archive exceeds the 512 MiB limit.");
            archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: false);
        }
        catch { stream.Dispose(); throw; }
        try
        {
            if (archive.Entries.Count > MaxEntries) throw new InvalidDataException("Quake archive contains too many entries.");
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            long total = 0;
            foreach (var entry in archive.Entries)
            {
                string name = entry.FullName.Replace('\\', '/');
                string path = name.TrimEnd('/');
                if (entry.FullName.Contains('\\') || path.Length == 0 || name.StartsWith('/') || name.Contains(':')
                    || path.Split('/').Any(part => part is "" or "." or "..") || name.Any(char.IsControl))
                    throw new InvalidDataException("Quake archive contains an unsafe entry path.");
                if (!names.Add(path)) throw new InvalidDataException("Quake archive contains duplicate entry paths.");
                if (entry.Length > MaxEntryBytes || entry.Length < 0
                    || (entry.Length > 1024 * 1024 && entry.Length / Math.Max(1, entry.CompressedLength) > 1000))
                    throw new InvalidDataException("Quake archive entry exceeds decompression limits.");
                total = checked(total + entry.Length);
                if (total > MaxExpandedBytes) throw new InvalidDataException("Quake archive exceeds the expanded size limit.");
                if (((entry.ExternalAttributes >> 16) & 0xF000) == 0xA000)
                    throw new InvalidDataException("Quake archive contains a symbolic link.");
            }
            if (stream.Length > MaxArchiveBytes)
                throw new InvalidDataException("Quake archive grew beyond the 512 MiB limit.");
            return archive;
        }
        catch { archive.Dispose(); throw; }
    }

    public static string Fingerprint(Q3ImportService.Options options, string selectedMap,
        IReadOnlyList<string> archives, CancellationToken cancellation = default, string converterVersion = ConverterVersion)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        void Add(string value)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(value);
            Span<byte> length = stackalloc byte[sizeof(int)];
            BinaryPrimitives.WriteInt32LittleEndian(length, bytes.Length);
            hash.AppendData(length);
            hash.AppendData(bytes);
        }
        Add(converterVersion);
        Add(MapBuildFingerprint.CurrentCompilerVersion.ToString(System.Globalization.CultureInfo.InvariantCulture));
        Add(MapBuildFingerprint.CurrentGeneratorHash);
        Add(selectedMap.ToLowerInvariant());
        Add(JsonSerializer.Serialize(options with { Source = "", MapName = selectedMap, DestinationDirectory = "", Dependencies = null }));

        string sourcePath = Path.GetFullPath(options.Source);
        bool SamePath(string left, string right) => StringComparerForPaths.Equals(left, right);
        byte[] buffer = new byte[81920];
        void AddFileContents(string path)
        {
            cancellation.ThrowIfCancellationRequested();
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
                bufferSize: 81920, FileOptions.SequentialScan);
            long limit = Path.GetExtension(path).Equals(".bsp", StringComparison.OrdinalIgnoreCase) ? MaxEntryBytes : MaxArchiveBytes;
            long expected = stream.Length;
            if (expected > limit) throw new InvalidDataException("Quake source exceeds the fingerprint size limit.");
            Span<byte> length = stackalloc byte[sizeof(long)];
            BinaryPrimitives.WriteInt64LittleEndian(length, expected);
            hash.AppendData(length);
            long consumed = 0; int read;
            while ((read = stream.Read(buffer)) > 0)
            {
                cancellation.ThrowIfCancellationRequested();
                consumed += read;
                if (consumed > limit) throw new InvalidDataException("Quake source grew beyond the fingerprint size limit.");
                hash.AppendData(buffer.AsSpan(0, read));
            }
            if (consumed != expected)
                throw new IOException("Quake source changed while its conversion fingerprint was being computed.");
            cancellation.ThrowIfCancellationRequested();
        }

        Add("source");
        AddFileContents(sourcePath);
        Add("source-name");
        Add(Path.GetFileName(options.Source));
        Add("archive-count");
        Add(archives.Count.ToString(System.Globalization.CultureInfo.InvariantCulture));
        for (int i = 0; i < archives.Count; i++)
        {
            cancellation.ThrowIfCancellationRequested();
            string path = Path.GetFullPath(archives[i]);
            // Archive order is significant: the first archive resolves duplicate textures.
            // The selected source is already in the hash stream above; its archive role is
            // represented by this marker so it is not read and hashed a second time.
            Add("archive");
            Add(i.ToString(System.Globalization.CultureInfo.InvariantCulture));
            Add(Path.GetExtension(path).ToLowerInvariant());
            if (SamePath(path, sourcePath)) Add("same-as-source");
            else
            {
                Add("archive-bytes");
                AddFileContents(path);
            }
        }
        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }

    internal static bool Restore(string key, string staging, CancellationToken cancellation)
    {
        lock (CacheLock)
        {
            string cacheRoot = CacheRoot;
            Directory.CreateDirectory(cacheRoot);
            using var lease = AcquireCacheLease(cacheRoot, cancellation);
            CleanupOrphanedPending(cacheRoot, cancellation);
            string artifact = Path.Combine(cacheRoot, key);
            if (!Directory.Exists(artifact)) return false;
            bool InvalidArtifact() { Directory.Delete(artifact, true); return false; }
            string manifest = Path.Combine(artifact, ".cache-hashes");
            if (!File.Exists(manifest) || new FileInfo(manifest).Length > 4 * 1024 * 1024) return InvalidArtifact();
            Dictionary<string, string>? hashes;
            try { hashes = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(manifest)); }
            catch (JsonException) { return InvalidArtifact(); }
            if (hashes == null) return InvalidArtifact();
            var files = Directory.EnumerateFiles(artifact, "*", SearchOption.AllDirectories)
                .Where(f => f != manifest).ToArray();
            if (files.Length > MaxEntries || files.Sum(f => new FileInfo(f).Length) + new FileInfo(manifest).Length > MaxArchiveBytes
                || files.Length != hashes.Count || files.Any(f => !hashes.TryGetValue(Path.GetRelativePath(artifact, f), out string? expected)
                || MapBuildFingerprint.HashFile(f) != expected)) return InvalidArtifact();
            Copy(artifact, staging, cancellation);
            return true;
        }
    }

    internal static void Store(string key, string staging, CancellationToken cancellation)
    {
        lock (CacheLock)
        {
            string cacheRoot = CacheRoot;
            Directory.CreateDirectory(cacheRoot);
            using var lease = AcquireCacheLease(cacheRoot, cancellation);
            CleanupOrphanedPending(cacheRoot, cancellation);
            string artifact = Path.Combine(cacheRoot, key);
            if (Directory.Exists(artifact)) return;
            string temp = artifact + ".pending-" + Guid.NewGuid().ToString("N");
            try
            {
                Copy(staging, temp, cancellation);
                var hashes = Directory.EnumerateFiles(temp, "*", SearchOption.AllDirectories)
                    .ToDictionary(f => Path.GetRelativePath(temp, f), MapBuildFingerprint.HashFile);
                File.WriteAllText(Path.Combine(temp, ".cache-hashes"), JsonSerializer.Serialize(hashes));
                long bytes = Directory.EnumerateFiles(temp, "*", SearchOption.AllDirectories).Sum(f => new FileInfo(f).Length);
                if (bytes > MaxArchiveBytes) return;
                var entries = new DirectoryInfo(cacheRoot).GetDirectories()
                    .Where(d => !d.Name.Contains(".pending-")).OrderBy(d => d.CreationTimeUtc).ToList();
                long total = entries.Sum(d => d.EnumerateFiles("*", SearchOption.AllDirectories).Sum(f => f.Length));
                while (entries.Count >= 8 || total + bytes > MaxArchiveBytes)
                {
                    var oldest = entries[0]; entries.RemoveAt(0);
                    total -= oldest.EnumerateFiles("*", SearchOption.AllDirectories).Sum(f => f.Length);
                    oldest.Delete(true);
                }
                Directory.Move(temp, artifact);
            }
            finally { if (Directory.Exists(temp)) Directory.Delete(temp, true); }
        }
    }

    private static FileStream AcquireCacheLease(string cacheRoot, CancellationToken cancellation)
    {
        string leasePath = Path.Combine(cacheRoot, CacheLeaseFileName);
        var timer = Stopwatch.StartNew();
        while (true)
        {
            cancellation.ThrowIfCancellationRequested();
            try { return new FileStream(leasePath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
            catch (IOException ex)
            {
                if (timer.Elapsed >= CacheLeaseTimeout)
                    throw new IOException("Timed out waiting for the Q3 conversion cache publication lease.", ex);
                cancellation.WaitHandle.WaitOne(TimeSpan.FromMilliseconds(25));
            }
        }
    }

    private static void CleanupOrphanedPending(string cacheRoot, CancellationToken cancellation)
    {
        foreach (string directory in Directory.EnumerateDirectories(cacheRoot, "*", SearchOption.TopDirectoryOnly))
        {
            cancellation.ThrowIfCancellationRequested();
            string name = Path.GetFileName(directory);
            const string marker = ".pending-";
            int markerIndex = name.IndexOf(marker, StringComparison.Ordinal);
            if (markerIndex != 64 || name.Length != markerIndex + marker.Length + 32
                || !IsLowerHex(name.AsSpan(0, markerIndex))
                || !IsLowerHex(name.AsSpan(markerIndex + marker.Length)))
                continue;
            Directory.Delete(directory, recursive: true);
        }
    }

    private static bool IsLowerHex(ReadOnlySpan<char> value)
    {
        foreach (char c in value)
            if (!(c is >= '0' and <= '9' or >= 'a' and <= 'f')) return false;
        return true;
    }

    private static void Copy(string source, string destination, CancellationToken cancellation)
    {
        Directory.CreateDirectory(destination);
        foreach (string file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            cancellation.ThrowIfCancellationRequested();
            if (Path.GetFileName(file) == ".cache-hashes") continue;
            string target = Path.Combine(destination, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, false);
        }
    }
}

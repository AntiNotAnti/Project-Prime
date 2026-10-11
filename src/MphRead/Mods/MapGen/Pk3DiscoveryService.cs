using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;

namespace MphRead.Mods.MapGen;

/// <summary>Local archive discovery and immutable, bounded Q3 conversion artifacts.</summary>
public static class Pk3DiscoveryService
{
    public const string ConverterVersion = "prime-q3-2";
    public const long MaxArchiveBytes = 512L * 1024 * 1024;
    public const long MaxExpandedBytes = 1024L * 1024 * 1024;
    public const long MaxEntryBytes = 128L * 1024 * 1024;
    public const int MaxEntries = 20000;
    private static readonly object CacheLock = new();
    private static readonly string DefaultCacheRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ProjectPrime", "q3-conversion-cache");
    internal static string? CacheDirectoryOverride { get; set; }
    private static string CacheRoot => CacheDirectoryOverride ?? DefaultCacheRoot;
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
        void Add(string value) => hash.AppendData(Encoding.UTF8.GetBytes(value + "\0"));
        Add(converterVersion);
        Add(MapBuildFingerprint.CurrentCompilerVersion.ToString(System.Globalization.CultureInfo.InvariantCulture));
        Add(selectedMap.ToLowerInvariant());
        Add(Path.GetFileName(options.Source));
        Add(JsonSerializer.Serialize(options with { Source = "", MapName = selectedMap, DestinationDirectory = "", Dependencies = null }));
        foreach (string path in new[] { options.Source }.Concat(archives))
        {
            cancellation.ThrowIfCancellationRequested();
            // Archive order is significant: the first archive resolves duplicate textures.
            Add(Path.GetFullPath(path));
            using var stream = File.OpenRead(path);
            long limit = Path.GetExtension(path).Equals(".bsp", StringComparison.OrdinalIgnoreCase) ? MaxEntryBytes : MaxArchiveBytes;
            if (stream.Length > limit) throw new InvalidDataException("Quake source exceeds the fingerprint size limit.");
            long consumed = 0;
            byte[] buffer = new byte[81920]; int read;
            while ((read = stream.Read(buffer)) > 0)
            {
                cancellation.ThrowIfCancellationRequested();
                consumed += read;
                if (consumed > limit) throw new InvalidDataException("Quake source grew beyond the fingerprint size limit.");
                hash.AppendData(buffer.AsSpan(0, read));
            }
            Add("end");
        }
        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }

    internal static bool Restore(string key, string staging, CancellationToken cancellation)
    {
        lock (CacheLock)
        {
            string artifact = Path.Combine(CacheRoot, key);
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
            Directory.CreateDirectory(CacheRoot);
            string artifact = Path.Combine(CacheRoot, key);
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
                var entries = new DirectoryInfo(CacheRoot).GetDirectories()
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

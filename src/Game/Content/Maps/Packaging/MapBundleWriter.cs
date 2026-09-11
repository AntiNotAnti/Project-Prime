using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace MphRead.Mods.MapGen;

public sealed class MapBundleWriter
{
    private static readonly DateTimeOffset DeterministicTimestamp = new(1980, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private readonly MapBundleLimits _limits;

    public MapBundleWriter(MapBundleLimits? limits = null)
        => _limits = limits ?? new MapBundleLimits();

    public async Task<MapBundleWriteResult> WriteAsync(string destinationPath, MapManifest manifest,
        IEnumerable<MapPackageFile> files, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(files);
        if (manifest.Format != MapManifest.CurrentFormat)
            throw new MapPackageException("MAP-PKG-005", "Only manifest v2 bundles can be written.");
        _ = new MapIdentity(manifest.StableId, manifest.Version);

        List<MapPackageFile> ordered = files
            .Select(file => file with { Path = MapBundlePath.Canonicalize(file.Path) })
            .OrderBy(file => file.Path, StringComparer.Ordinal)
            .ToList();
        if (ordered.Count == 0)
            throw new MapPackageException("MAP-PKG-004", "A map bundle cannot be empty.");
        if (ordered.Count + 1 > _limits.MaximumFileCount)
            throw new MapPackageException("MAP-PKG-007",
                $"Map bundle contains too many files; the limit is {_limits.MaximumFileCount} including manifest.json.");
        if (ordered.Any(file => file.Data.Length > _limits.MaximumEntryBytes)
            || ordered.Sum(file => (long)file.Data.Length) > _limits.MaximumDecompressedBytes)
            throw new MapPackageException("MAP-PKG-007", "Map bundle exceeds an entry or total decompressed-size limit.");
        if (ordered.Any(file => file.Path == MapBundle.ManifestPath))
            throw new MapPackageException("MAP-PKG-003", "manifest.json is reserved.");
        if (ordered.Select(file => file.Path).Distinct(StringComparer.Ordinal).Count() != ordered.Count
            || ordered.Select(file => file.Path).Distinct(StringComparer.OrdinalIgnoreCase).Count() != ordered.Count)
            throw new MapPackageException("MAP-PKG-003", "Map bundle paths must be unique without case collisions.");

        string recipe = MapBundlePath.Canonicalize(manifest.Recipe);
        if (ordered.Count(file => file.Role == MapFileRole.Recipe) != 1
            || !ordered.Any(file => file.Role == MapFileRole.Recipe && file.Path == recipe))
            throw new MapPackageException("MAP-PKG-006", "Bundle input must contain exactly the declared recipe.");
        if (manifest.Preview != null)
        {
            string preview = MapBundlePath.Canonicalize(manifest.Preview);
            if (!ordered.Any(file => file.Role == MapFileRole.Preview && file.Path == preview))
                throw new MapPackageException("MAP-PKG-006", "Bundle input does not contain the declared preview.");
        }

        manifest.Files = ordered.Select(file => new MapManifestFile
        {
            Path = file.Path,
            Size = file.Data.Length,
            Sha256 = MapJson.Sha256(file.Data.Span),
            Role = file.Role
        }).ToList();
        manifest.ContentHash = MapContentHasher.Compute(manifest);
        byte[] manifestBytes = JsonSerializer.SerializeToUtf8Bytes(manifest, MapJsonContext.Default.MapManifest);

        string fullPath = Path.GetFullPath(destinationPath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        string temporaryPath = fullPath + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            await using (var output = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write,
                FileShare.None, 64 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan))
            using (var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
            {
                WriteEntry(archive, MapBundle.ManifestPath, manifestBytes);
                foreach (MapPackageFile file in ordered)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    WriteEntry(archive, file.Path, file.Data.Span);
                }
            }
            cancellationToken.ThrowIfCancellationRequested();
            if (new FileInfo(temporaryPath).Length > _limits.MaximumArtifactBytes)
                throw new MapPackageException("MAP-PKG-007", "Map bundle exceeds the artifact-size limit.");
            MapBundleReadResult verified = new MapBundleReader(new MapBundleReadOptions
            {
                AllowLegacyV1 = false,
                Limits = _limits
            }).Read(temporaryPath);
            File.Move(temporaryPath, fullPath, overwrite: true);
            return new MapBundleWriteResult(fullPath,
                new MapContentIdentity(verified.Manifest.Identity, verified.Manifest.ContentHash),
                verified.ArtifactHash, verified.PackageSize);
        }
        catch
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
            throw;
        }
    }

    private static void WriteEntry(ZipArchive archive, string path, ReadOnlySpan<byte> bytes)
    {
        ZipArchiveEntry entry = archive.CreateEntry(path, CompressionLevel.SmallestSize);
        entry.LastWriteTime = DeterministicTimestamp;
        entry.ExternalAttributes = 0x81A4 << 16; // regular file, 0644
        using Stream stream = entry.Open();
        stream.Write(bytes);
    }
}

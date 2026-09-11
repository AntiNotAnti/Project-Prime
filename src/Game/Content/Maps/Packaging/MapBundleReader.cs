using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;

namespace MphRead.Mods.MapGen;

public sealed class MapBundleReader
{
    private static readonly HashSet<string> ManifestMembers = new(StringComparer.Ordinal)
    {
        "format", "stableId", "version", "name", "author", "description", "supportedModes",
        "redistribution", "recipe", "preview", "contentHash", "files"
    };
    private static readonly HashSet<string> FileMembers = new(StringComparer.Ordinal)
    {
        "path", "size", "sha256", "role"
    };

    private readonly MapBundleReadOptions _options;

    public MapBundleReader(MapBundleReadOptions? options = null)
        => _options = options ?? new MapBundleReadOptions();

    public MapBundleReadResult Read(string bundlePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(bundlePath);
        string fullPath = Path.GetFullPath(bundlePath);
        if (!File.Exists(fullPath))
            throw new MapPackageException("MAP-PKG-001", $"Map bundle does not exist: {fullPath}");
        long artifactLength;
        try { artifactLength = new FileInfo(fullPath).Length; }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new MapPackageException("MAP-PKG-001", $"Map bundle could not be inspected: {fullPath}", exception);
        }
        if (artifactLength is < 1 || artifactLength > _options.Limits.MaximumArtifactBytes)
            throw new MapPackageException("MAP-PKG-007",
                $"Map bundle artifact size must be 1..{_options.Limits.MaximumArtifactBytes:N0} bytes.");

        byte[] artifactBytes;
        try
        {
            artifactBytes = File.ReadAllBytes(fullPath);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new MapPackageException("MAP-PKG-001", $"Map bundle could not be read: {fullPath}", exception);
        }
        string artifactHash = MapJson.Sha256(artifactBytes);

        try
        {
            using var memory = new MemoryStream(artifactBytes, writable: false);
            using var archive = new ZipArchive(memory, ZipArchiveMode.Read, leaveOpen: false);
            Dictionary<string, byte[]> entries = ReadEntries(archive);
            if (entries.TryGetValue(MapBundle.ManifestPath, out byte[]? manifestBytes))
            {
                return ReadV2(entries, manifestBytes, artifactHash, artifactBytes.LongLength);
            }
            if (!_options.AllowLegacyV1)
                throw new MapPackageException("MAP-PKG-004", "Map bundle is missing manifest.json.");
            return ReadLegacy(entries, artifactHash, artifactBytes.LongLength);
        }
        catch (MapPackageException)
        {
            throw;
        }
        catch (Exception exception) when (exception is InvalidDataException or IOException or JsonException)
        {
            throw new MapPackageException("MAP-PKG-011", "Map bundle is corrupt or unreadable.", exception);
        }
    }

    private Dictionary<string, byte[]> ReadEntries(ZipArchive archive)
    {
        MapBundleLimits limits = _options.Limits;
        if (archive.Entries.Count == 0)
            throw new MapPackageException("MAP-PKG-004", "Map bundle is empty.");
        if (archive.Entries.Count > limits.MaximumFileCount)
            throw new MapPackageException("MAP-PKG-007",
                $"Map bundle contains {archive.Entries.Count} files; the limit is {limits.MaximumFileCount}.");

        var exact = new HashSet<string>(StringComparer.Ordinal);
        var insensitive = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        long total = 0;
        foreach (ZipArchiveEntry entry in archive.Entries)
        {
            string path = MapBundlePath.Canonicalize(entry.FullName);
            if (IsSymbolicLink(entry))
                throw new MapPackageException("MAP-PKG-003", $"Symbolic links are forbidden in map bundles: '{path}'.");
            if (!exact.Add(path))
                throw new MapPackageException("MAP-PKG-003", $"Map bundle contains duplicate path '{path}'.");
            if (!insensitive.Add(path))
                throw new MapPackageException("MAP-PKG-003", $"Map bundle contains case-colliding path '{path}'.");
            if (entry.Length < 0 || entry.Length > limits.MaximumEntryBytes)
                throw new MapPackageException("MAP-PKG-007", $"Map bundle entry '{path}' is too large.");
            if (entry.Length > limits.MaximumDecompressedBytes - total)
                throw new MapPackageException("MAP-PKG-007", "Map bundle exceeds the decompressed-size limit.");
            total += entry.Length;
            if (entry.Length > 0 && (entry.CompressedLength == 0
                || entry.Length / Math.Max(1, entry.CompressedLength) > limits.MaximumCompressionRatio))
                throw new MapPackageException("MAP-PKG-008", $"Map bundle entry '{path}' has a suspicious compression ratio.");

            using Stream stream = entry.Open();
            byte[] bytes = GC.AllocateUninitializedArray<byte>(checked((int)entry.Length));
            stream.ReadExactly(bytes);
            if (stream.ReadByte() != -1)
                throw new MapPackageException("MAP-PKG-007", $"Map bundle entry '{path}' exceeds its declared size.");
            result.Add(path, bytes);
        }
        return result;
    }

    private static MapBundleReadResult ReadV2(Dictionary<string, byte[]> entries,
        byte[] manifestBytes, string artifactHash, long packageSize)
    {
        using JsonDocument document = MapJson.ParseStrict(manifestBytes);
        RequireOnlyMembers(document.RootElement, ManifestMembers, "manifest");
        if (document.RootElement.TryGetProperty("files", out JsonElement filesElement)
            && filesElement.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement file in filesElement.EnumerateArray())
                RequireOnlyMembers(file, FileMembers, "manifest file");
        }

        MapManifest manifest;
        try
        {
            manifest = JsonSerializer.Deserialize(manifestBytes, MapJsonContext.Default.MapManifest)
                ?? throw new JsonException("Manifest is null.");
        }
        catch (Exception exception) when (exception is JsonException or NotSupportedException or FormatException or ArgumentException)
        {
            throw new MapPackageException("MAP-PKG-012", "Map manifest is malformed.", exception);
        }
        ValidateManifest(manifest, entries);
        return new MapBundleReadResult
        {
            Manifest = manifest,
            Files = new ReadOnlyDictionary<string, byte[]>(entries
                .Where(pair => pair.Key != MapBundle.ManifestPath)
                .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal)),
            ArtifactHash = artifactHash,
            PackageSize = packageSize
        };
    }

    private static MapBundleReadResult ReadLegacy(Dictionary<string, byte[]> entries,
        string artifactHash, long packageSize)
    {
        string[] recipes = entries.Keys
            .Where(path => Path.GetExtension(path).Equals(".json", StringComparison.OrdinalIgnoreCase))
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();
        if (recipes.Length != 1)
            throw new MapPackageException("MAP-PKG-004",
                $"Legacy map bundle must contain exactly one JSON recipe; found {recipes.Length}.");
        using JsonDocument _ = MapJson.ParseStrict(entries[recipes[0]]);
        MapDefinition definition;
        try
        {
            definition = MapDefinition.DeserializeLegacy(
                System.Text.Encoding.UTF8.GetString(entries[recipes[0]]), recipes[0]);
        }
        catch (Exception exception) when (exception is JsonException or NotSupportedException)
        {
            throw new MapPackageException("MAP-PKG-012", "Legacy map recipe is malformed.", exception);
        }
        if (definition.Import == null && definition.Brushes.Count == 0)
            throw new MapPackageException("MAP-PKG-012", "Legacy map recipe contains no geometry source.");

        var manifest = new MapManifest
        {
            Format = 1,
            StableId = MapIdentity.FromLegacyName(definition.Name),
            Version = new MapVersion(1, 0, 0),
            Name = definition.InGameName ?? definition.Name,
            Author = "Unknown",
            Description = "Legacy Project Prime map bundle.",
            SupportedModes = [MapMode.Battle, MapMode.Survival],
            Redistribution = false,
            Recipe = recipes[0]
        };
        foreach ((string path, byte[] bytes) in entries.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            MapFileRole role = path == recipes[0] ? MapFileRole.Recipe
                : definition.Import?.Textures != null && path.Equals(definition.Import.Textures, StringComparison.Ordinal)
                    ? MapFileRole.Textures
                    : Path.GetExtension(path).Equals(".bsp", StringComparison.OrdinalIgnoreCase)
                        ? MapFileRole.Geometry
                        : Path.GetExtension(path).Equals(".png", StringComparison.OrdinalIgnoreCase)
                            ? MapFileRole.Preview
                            : MapFileRole.Metadata;
            manifest.Files.Add(new MapManifestFile
            {
                Path = path,
                Size = bytes.LongLength,
                Sha256 = MapJson.Sha256(bytes),
                Role = role
            });
        }
        manifest.ContentHash = MapContentHasher.Compute(manifest);
        return new MapBundleReadResult
        {
            Manifest = manifest,
            Files = new ReadOnlyDictionary<string, byte[]>(entries),
            ArtifactHash = artifactHash,
            PackageSize = packageSize,
            IsLegacy = true
        };
    }

    private static void ValidateManifest(MapManifest manifest, Dictionary<string, byte[]> entries)
    {
        if (manifest.Format != MapManifest.CurrentFormat)
            throw new MapPackageException("MAP-PKG-005", $"Unsupported map manifest format {manifest.Format}.");
        _ = new MapIdentity(manifest.StableId, manifest.Version);
        if (string.IsNullOrWhiteSpace(manifest.Name) || string.IsNullOrWhiteSpace(manifest.Author))
            throw new MapPackageException("MAP-PKG-012", "Map name and author are required.");
        if (manifest.SupportedModes.Count == 0 || manifest.SupportedModes.Count != manifest.SupportedModes.Distinct().Count())
            throw new MapPackageException("MAP-PKG-012", "Map supported modes must be non-empty and unique.");

        string recipe = MapBundlePath.Canonicalize(manifest.Recipe);
        if (!recipe.Equals(manifest.Recipe, StringComparison.Ordinal))
            throw new MapPackageException("MAP-PKG-002", "Manifest recipe path is not canonical.");
        string? preview = manifest.Preview == null ? null : MapBundlePath.Canonicalize(manifest.Preview);
        if (preview != manifest.Preview)
            throw new MapPackageException("MAP-PKG-002", "Manifest preview path is not canonical.");

        var declared = new HashSet<string>(StringComparer.Ordinal);
        var declaredInsensitive = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (MapManifestFile file in manifest.Files)
        {
            string path = MapBundlePath.Canonicalize(file.Path);
            if (path == MapBundle.ManifestPath)
                throw new MapPackageException("MAP-PKG-003", "manifest.json must not declare itself.");
            if (!declared.Add(path) || !declaredInsensitive.Add(path))
                throw new MapPackageException("MAP-PKG-003", $"Manifest declares duplicate or case-colliding path '{path}'.");
            if (!entries.TryGetValue(path, out byte[]? bytes))
                throw new MapPackageException("MAP-PKG-006", $"Manifest-declared file '{path}' is missing.");
            if (file.Size != bytes.LongLength)
                throw new MapPackageException("MAP-PKG-009", $"Size mismatch for bundle file '{path}'.");
            string expected = MapHash.Validate(file.Sha256, nameof(file.Sha256));
            if (!CryptographicOperations.FixedTimeEquals(
                Convert.FromHexString(expected), SHA256.HashData(bytes)))
                throw new MapPackageException("MAP-PKG-009", $"SHA-256 mismatch for bundle file '{path}'.");
            if (Path.GetExtension(path).Equals(".json", StringComparison.OrdinalIgnoreCase)
                && file.Role is not (MapFileRole.Recipe or MapFileRole.Metadata))
                throw new MapPackageException("MAP-PKG-010", $"Unexpected JSON role for '{path}'.");
        }
        string[] extras = entries.Keys
            .Where(path => path != MapBundle.ManifestPath && !declared.Contains(path)).ToArray();
        if (extras.Length != 0)
            throw new MapPackageException("MAP-PKG-009", $"Bundle contains undeclared file '{extras[0]}'.");
        if (manifest.Files.Count(file => file.Role == MapFileRole.Recipe) != 1
            || !manifest.Files.Any(file => file.Role == MapFileRole.Recipe && file.Path == recipe))
            throw new MapPackageException("MAP-PKG-006", "Manifest must declare exactly one recipe at its recipe path.");
        if (preview != null && !manifest.Files.Any(file => file.Role == MapFileRole.Preview && file.Path == preview))
            throw new MapPackageException("MAP-PKG-006", "Manifest preview path is not declared with the preview role.");
        string actualContentHash = MapContentHasher.Compute(manifest);
        if (!CryptographicOperations.FixedTimeEquals(
            Convert.FromHexString(MapHash.Validate(manifest.ContentHash, nameof(manifest.ContentHash))),
            Convert.FromHexString(actualContentHash)))
            throw new MapPackageException("MAP-PKG-009", "Map logical content hash does not match its manifest.");
    }

    private static void RequireOnlyMembers(JsonElement element, HashSet<string> allowed, string description)
    {
        if (element.ValueKind != JsonValueKind.Object)
            throw new MapPackageException("MAP-PKG-012", $"The {description} must be a JSON object.");
        foreach (JsonProperty property in element.EnumerateObject())
        {
            if (!allowed.Contains(property.Name))
                throw new MapPackageException("MAP-PKG-012", $"Unknown {description} member '{property.Name}'.");
        }
    }

    private static bool IsSymbolicLink(ZipArchiveEntry entry)
    {
        const int UnixFileTypeMask = 0xF000;
        const int UnixSymbolicLink = 0xA000;
        int unixMode = entry.ExternalAttributes >> 16;
        return (unixMode & UnixFileTypeMask) == UnixSymbolicLink;
    }
}

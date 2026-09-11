using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace MphRead.Mods.MapGen;

[JsonConverter(typeof(JsonStringEnumConverter<MapFileRole>))]
public enum MapFileRole
{
    Recipe,
    Geometry,
    Textures,
    Preview,
    Metadata
}

public sealed class MapManifest
{
    public const int CurrentFormat = 2;

    public int Format { get; set; } = CurrentFormat;
    public string StableId { get; set; } = "community.new-map";
    public MapVersion Version { get; set; } = new(1, 0, 0);
    public string Name { get; set; } = "New Map";
    public string Author { get; set; } = "Unknown";
    public string Description { get; set; } = "";
    public List<MapMode> SupportedModes { get; set; } = [MapMode.Battle, MapMode.Survival];
    public bool Redistribution { get; set; }
    public string Recipe { get; set; } = "map.json";
    public string? Preview { get; set; }
    public string ContentHash { get; set; } = new string('0', 64);
    public List<MapManifestFile> Files { get; set; } = [];

    [JsonIgnore]
    public MapIdentity Identity => new(StableId, Version);
}

public sealed class MapManifestFile
{
    public string Path { get; set; } = "";
    public long Size { get; set; }
    public string Sha256 { get; set; } = new string('0', 64);
    public MapFileRole Role { get; set; }
}

public sealed record MapPackageFile(string Path, MapFileRole Role, ReadOnlyMemory<byte> Data);

public sealed record MapBundleWriteResult(
    string Path,
    MapContentIdentity ContentIdentity,
    string ArtifactHash,
    long PackageSize);

public sealed class MapBundleReadResult
{
    public required MapManifest Manifest { get; init; }
    public required IReadOnlyDictionary<string, byte[]> Files { get; init; }
    public required string ArtifactHash { get; init; }
    public required long PackageSize { get; init; }
    public bool IsLegacy { get; init; }

    public byte[] ReadDeclaredFile(string path)
    {
        string canonical = MapBundlePath.Canonicalize(path);
        if (!Files.TryGetValue(canonical, out byte[]? bytes))
            throw new MapPackageException("MAP-PKG-006", $"Bundle does not contain declared file '{canonical}'.");
        return (byte[])bytes.Clone();
    }
}

public sealed class MapBundleLimits
{
    public int MaximumFileCount { get; init; } = 512;
    public long MaximumArtifactBytes { get; init; } = 256L * 1024 * 1024;
    public long MaximumEntryBytes { get; init; } = 64L * 1024 * 1024;
    public long MaximumDecompressedBytes { get; init; } = 256L * 1024 * 1024;
    public int MaximumCompressionRatio { get; init; } = 200;
}

public sealed class MapBundleReadOptions
{
    public MapBundleLimits Limits { get; init; } = new();
    public bool AllowLegacyV1 { get; init; } = true;
}

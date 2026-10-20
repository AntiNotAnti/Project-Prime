using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using MphRead.Mods.Import;

namespace MphRead.Mods.MapGen;

/// <summary>Detached room-generation identity. Capture performs no content IO.</summary>
internal sealed class MapSourceGeometryReadSpec
{
    internal Guid MapId { get; }
    internal string RecipeSha256 { get; }
    internal string ComponentPath { get; }
    internal string ManifestPath { get; }
    private MapSourceGeometryReadSpec(Guid id, string recipe, string component, string manifest)
    { MapId = id; RecipeSha256 = recipe; ComponentPath = component; ManifestPath = manifest; }

    internal static MapSourceGeometryReadSpec? Capture(MapDefinition definition, string archive)
    {
        if (!definition.HasSourceGeometry) return null;
        if (definition.BundlePath != null || definition.MapId == Guid.Empty)
            throw new InvalidDataException("Source coordinates require a private loose map with a persistent identity.");
        string prefix = definition.Name.ToLowerInvariant();
        return new(definition.MapId, MapBuildFingerprint.HashText(definition.Serialize()),
            $@"_archives\{archive}\{prefix}_SourceGeometry.bin", $@"_archives\{archive}\map.build.json");
    }

    internal MapSourceGeometry.Snapshot Load(string root, ReadOnlySpan<byte> exactModelBytes)
    {
        if (exactModelBytes.Length > MapSourceGeometry.MaxNativeModelBytes)
            throw new InvalidDataException("Source native model exceeds its extent budget.");
        string component = Paths.Combine(root, ComponentPath), manifest = Paths.Combine(root, ManifestPath);
        // Compiled outputs use the same guarded root as their model. The
        // manifest brackets this read so concurrent publication cannot adopt a
        // model/component pair from different generations, including prewarm.
        LocalImportBundle.CheckPath(component, root); LocalImportBundle.CheckPath(manifest, root);
        byte[] before = BoundedRead(manifest, 64 * 1024);
        var receipt = JsonSerializer.Deserialize<MapBuildManifest>(before)
            ?? throw new InvalidDataException("Source geometry build manifest is missing.");
        if (receipt.Fingerprint?.RecipeHash != RecipeSha256
            || receipt.Fingerprint.CompilerVersion != MapBuildFingerprint.SourceGeometryCompilerVersion
            || receipt.OutputHashes?.Length != 6
            || receipt.OutputHashes[0] != Hash(exactModelBytes))
            throw new InvalidDataException("Source geometry build manifest belongs to a different model or recipe.");
        byte[] bytes = BoundedRead(component, MapSourceGeometry.MaxComponentBytes);
        if (receipt.OutputHashes[5] != Hash(bytes))
            throw new InvalidDataException("Source geometry component changed after publication.");
        var result = MapSourceGeometry.Decode(bytes, exactModelBytes, MapId, RecipeSha256);
        LocalImportBundle.CheckPath(component, root); LocalImportBundle.CheckPath(manifest, root);
        if (!before.AsSpan().SequenceEqual(BoundedRead(manifest, 64 * 1024)))
            throw new InvalidDataException("Source geometry publication changed during model loading.");
        return result;
    }
    private static byte[] BoundedRead(string path, int maximum)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        long length = stream.Length;
        if (length < 1 || length > maximum) throw new InvalidDataException("Source geometry file exceeds its extent budget.");
        byte[] bytes = new byte[(int)length]; stream.ReadExactly(bytes);
        if (stream.ReadByte() != -1) throw new InvalidDataException("Source geometry file changed length during reading.");
        return bytes;
    }
    private static string Hash(ReadOnlySpan<byte> bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));
}

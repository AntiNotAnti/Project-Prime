using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace MphRead.Mods.MapGen;

public enum RadiantSourceState { Saved, ExternalChanges, NeedsReview, ImportConflict, UnsupportedForRadiant, ReadyForBuild }
public sealed record RadiantGeometryReview(string Source, string SourceHash, string SidecarHash,
    string GeometryBaseline, MapDefinition Proposed, int BeforeCount, int AfterCount);

/// <summary>Editor-only geometry overlay. Native JSON remains authoritative for
/// gameplay, imported architecture, collision, materials, assets and ownership.
/// Unsupported authored geometry is refused by the existing strict exporter.</summary>
public static class RadiantSourceWorkspace
{
    private sealed record Manifest(int Version, Guid MapId, string SourceHash,
        string GeometryHash, string NativeProjectJson);
    public static string SourcePath(string root, Guid mapId)
    {
        if (mapId == Guid.Empty) throw new InvalidDataException("Radiant workspace requires a MapId.");
        return Path.Combine(Path.GetFullPath(root), mapId.ToString("N"), "world.map");
    }
    public static string SidecarPath(string source) => Path.Combine(Path.GetDirectoryName(Path.GetFullPath(source))!, "project-prime.json");
    private static string ManifestPath(string source) => source + ".prime-source.json";
    public static string GeometryHash(MapDefinition definition)
        => MapBuildFingerprint.HashText(JsonSerializer.Serialize(new { definition.Geometry, definition.Materials }));

    public static string Prepare(MapDefinition definition, string root, Action<RadiantSourceCommitPoint>? fault = null)
    {
        string source = SourcePath(root, definition.MapId), sidecar = SidecarPath(source);
        using var sourceLease = RadiantSourceTransaction.Acquire(source);
        RadiantSourceTransaction.RecoverUnderLease(source, sidecar);
        if (File.Exists(source))
        {
            var existing = Read(source);
            if (existing.MapId != definition.MapId)
                throw new InvalidDataException("Radiant source belongs to another map.");
            // The author may have saved in Radiant. Opening never exports over it.
            return source;
        }
        var geometry = MapProjectSerializer.Clone(definition);
        geometry.Import = null; geometry.NativeRoom = null; geometry.Collision = null;
        geometry.Brushes.Clear(); geometry.Spawns.Clear(); geometry.Items.Clear();
        geometry.JumpPads.Clear(); geometry.NavigationLinks.Clear(); geometry.Q3Triggers = null;
        // Geometry remains intact: meshes/rotation/UVs outside the supported
        // subset fail with the existing actionable diagnostics.
        QuakeMapInterchange.ExportUnderLease(geometry, source, sidecar,
            hash => ManifestBytes(definition, hash), fault);
        return source;
    }

    public static RadiantSourceState State(MapDefinition definition, string source)
    {
        try
        {
            var manifest = Read(source);
            if (definition.MapId != manifest.MapId || GeometryHash(definition) != manifest.GeometryHash)
                return RadiantSourceState.ImportConflict;
            return MapBuildFingerprint.HashFile(source) == manifest.SourceHash
                ? RadiantSourceState.Saved : RadiantSourceState.ExternalChanges;
        }
        catch (IOException) { return RadiantSourceState.ImportConflict; }
        catch (InvalidDataException) { return RadiantSourceState.ImportConflict; }
        catch (JsonException) { return RadiantSourceState.ImportConflict; }
    }

    public static RadiantGeometryReview Preview(MapDefinition current, string source)
    {
        var manifest = Read(source);
        string baseline = GeometryHash(current);
        if (manifest.MapId != current.MapId || manifest.GeometryHash != baseline)
            throw new IOException("Import conflict: Studio geometry or material bindings changed since export. "
                + "Keep both sources and reconcile in native Studio or export a new source workspace.");
        string sourceHash = MapBuildFingerprint.HashFile(source);
        string sidecarHash = MapBuildFingerprint.HashFile(SidecarPath(source));
        var imported = QuakeMapInterchange.Import(source, SidecarPath(source)).Definition;
        if (MapBuildFingerprint.HashFile(source) != sourceHash
            || MapBuildFingerprint.HashFile(SidecarPath(source)) != sidecarHash)
            throw new IOException("Radiant source or identity changed while reading. Preview changes again.");
        if (imported.MapId != current.MapId || imported.Name != current.Name)
            throw new InvalidDataException("Source identity or runtime name differs from the open project.");
        if (imported.Spawns.Count > 0 || imported.Items.Count > 0)
            throw new InvalidDataException("This is a geometry-only source. Edit gameplay objects in native Studio; "
                + "source entities cannot be silently discarded.");
        var proposed = MapProjectSerializer.Clone(current);
        // Reimporting unchanged source is a true no-op, including legacy UV bytes.
        if (sourceHash != manifest.SourceHash)
        {
            int[] mapping = imported.Materials.Select(material =>
            {
                var matches = proposed.Materials.Select((value, index) => (value, index))
                    .Where(x => string.Equals(x.value.Name, material.Name, StringComparison.OrdinalIgnoreCase)).ToArray();
                if (matches.Length > 1) throw new InvalidDataException("Ambiguous native shader binding: " + material.Name);
                if (matches.Length == 1) return matches[0].index;
                proposed.Materials.Add(material); return proposed.Materials.Count - 1;
            }).ToArray();
            var nativeIds = current.Brushes.Select(x => x.Id).Concat(current.Spawns.Select(x => x.Id))
                .Concat(current.Items.Select(x => x.Id)).Concat(current.JumpPads.Select(x => x.Id))
                .Concat(current.NavigationLinks.Select(x => x.Id)).ToHashSet();
            foreach (var geometry in imported.Geometry)
            {
                if (nativeIds.Contains(geometry.Id)) throw new InvalidDataException("Source geometry ID conflicts with a native gameplay object.");
                geometry.Material = mapping[geometry.Material];
                if (geometry.QuakeFaces != null)
                    foreach (var face in geometry.QuakeFaces) face.Material = mapping[face.Material];
                var original = current.Geometry.FirstOrDefault(x => x.Id == geometry.Id);
                if (original != null)
                {
                    geometry.Label = original.Label; geometry.Layer = original.Layer;
                    geometry.Locked = original.Locked; geometry.Terrain = original.Terrain;
                    geometry.Shade = original.Shade;
                }
            }
            proposed.Geometry = imported.Geometry;
        }
        return new(source, sourceHash, sidecarHash, baseline, proposed, current.Geometry.Count, proposed.Geometry.Count);
    }

    public static void RequireFresh(MapDefinition current, RadiantGeometryReview review)
    {
        if (current.MapId != review.Proposed.MapId || GeometryHash(current) != review.GeometryBaseline
            || MapBuildFingerprint.HashFile(review.Source) != review.SourceHash
            || MapBuildFingerprint.HashFile(SidecarPath(review.Source)) != review.SidecarHash)
            throw new IOException("Source or Studio geometry changed during review. Preview changes again.");
    }

    public static void Accept(MapDefinition current, RadiantGeometryReview review)
    {
        if (MapBuildFingerprint.HashFile(review.Source) != review.SourceHash
            || MapBuildFingerprint.HashFile(SidecarPath(review.Source)) != review.SidecarHash)
            throw new IOException("Source changed after apply; review the newer revision.");
        SaveManifest(review.Source, current, review.SourceHash);
    }
    private static void SaveManifest(string source, MapDefinition definition, string expectedSourceHash)
    {
        if (!MapBuildFingerprint.HashFile(source).Equals(expectedSourceHash, StringComparison.OrdinalIgnoreCase))
            throw new IOException("Radiant source changed before its native baseline could be saved. Review the newer revision.");
        AtomicFile.Write(ManifestPath(source), ManifestBytes(definition, expectedSourceHash));
    }
    private static byte[] ManifestBytes(MapDefinition definition, string sourceHash)
    {
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(new Manifest(
            1, definition.MapId, sourceHash.ToLowerInvariant(), GeometryHash(definition), definition.Serialize()));
        if (bytes.Length > 16L * 1024 * 1024)
            throw new InvalidDataException("Native Radiant adjunct exceeds its 16 MiB limit. Keep this project in native Studio.");
        return bytes;
    }
    internal static void ValidateManifestBytes(byte[] bytes, Guid mapId, string sourceHash)
    {
        Manifest manifest;
        try { manifest = JsonSerializer.Deserialize<Manifest>(bytes)
            ?? throw new InvalidDataException("Invalid native source recovery metadata."); }
        catch (JsonException ex) { throw new InvalidDataException("Invalid native source recovery metadata.", ex); }
        if (manifest.Version != 1 || manifest.MapId != mapId
            || !string.Equals(manifest.SourceHash, sourceHash, StringComparison.OrdinalIgnoreCase)
            || manifest.GeometryHash is not { Length: 64 } || manifest.NativeProjectJson == null)
            throw new InvalidDataException("Native source recovery ownership or fingerprint differs.");
    }
    private static Manifest Read(string source)
    {
        string path = ManifestPath(source);
        if (!File.Exists(path) || new FileInfo(path).Length > 16L * 1024 * 1024)
            throw new InvalidDataException("Missing or oversized native Radiant adjunct. Keep source and import explicitly.");
        var result = JsonSerializer.Deserialize<Manifest>(File.ReadAllBytes(path))
            ?? throw new InvalidDataException("Invalid native Radiant adjunct.");
        if (result.Version != 1 || result.MapId == Guid.Empty)
            throw new InvalidDataException("Unsupported native Radiant adjunct.");
        if (QuakeMapInterchange.ReadIdentity(SidecarPath(source)).MapId != result.MapId)
            throw new InvalidDataException("Radiant adjunct and sidecar ownership differ.");
        return result;
    }
}

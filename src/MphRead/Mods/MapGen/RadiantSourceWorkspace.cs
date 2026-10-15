using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace MphRead.Mods.MapGen;

public enum RadiantSourceState { Saved, ExternalChanges, NeedsReview, ImportConflict, UnsupportedForRadiant, ReadyForBuild }
public sealed record RadiantGeometryReview(string Source, string SourceHash, string SidecarHash,
    string GeometryBaseline, MapDefinition Proposed, int BeforeCount, int AfterCount)
{
    public int WorkspaceVersion { get; init; } = 4;
    public string? ReferenceDependencyHash { get; init; }
    public Guid[] TriggerIds { get; init; } = Array.Empty<Guid>();
    public Guid[] ConvertedGeometryIds { get; init; } = Array.Empty<Guid>();
    public QuakeMapDiagnostic[] Diagnostics { get; init; } = Array.Empty<QuakeMapDiagnostic>();
}

/// <summary>Editable native geometry and gameplay source. Imported architecture,
/// custom collision, rich material assets and project metadata remain native.
/// Every owned list participates in the strict review conflict baseline.</summary>
public static class RadiantSourceWorkspace
{
    private sealed record Manifest(int Version, Guid MapId, string SourceHash,
        string GeometryHash, string NativeProjectJson, Guid[]? TriggerIds = null,
        RadiantReferenceScene.Identity[]? References = null, string? ReferenceDependencyHash = null);
    public static string SourcePath(string root, Guid mapId)
    {
        if (mapId == Guid.Empty) throw new InvalidDataException("Radiant workspace requires a MapId.");
        return Path.Combine(Path.GetFullPath(root), mapId.ToString("N"), "world.map");
    }
    public static string SidecarPath(string source) => Path.Combine(Path.GetDirectoryName(Path.GetFullPath(source))!, "project-prime.json");
    private static string ManifestPath(string source) => source + ".prime-source.json";
    public static string GeometryHash(MapDefinition definition)
        => MapBuildFingerprint.HashText(JsonSerializer.Serialize(new { definition.Geometry, definition.Materials }));

    private static string OwnedHash(MapDefinition definition, int version)
        => version < 3 ? GeometryHash(definition)
        : version >= 4 ? OwnedHashV4(definition) : MapBuildFingerprint.HashText(JsonSerializer.Serialize(new
        {
            definition.Geometry, definition.Materials, definition.Spawns, definition.Items,
            definition.JumpPads, definition.NavigationLinks, definition.Q3Triggers
        }));
    private static string OwnedHashV4(MapDefinition definition)
        => MapBuildFingerprint.HashText(JsonSerializer.Serialize(new
        {
            definition.Geometry, definition.Brushes, definition.Materials, definition.Spawns, definition.Items,
            definition.JumpPads, definition.NavigationLinks, definition.Q3Triggers
        }));
    private static bool ReferencesFresh(MapDefinition definition, Manifest manifest)
        => manifest.Version < 4 || manifest.ReferenceDependencyHash == RadiantReferenceScene.DependencyHash(definition);
    public static void ApplyReviewed(MapDefinition definition, RadiantGeometryReview review)
    {
        definition.Geometry = review.Proposed.Geometry;
        definition.Materials = review.Proposed.Materials;
        if (review.WorkspaceVersion >= 4) definition.Brushes = review.Proposed.Brushes;
        if (review.WorkspaceVersion >= 3)
        {
            definition.Spawns = review.Proposed.Spawns;
            definition.Items = review.Proposed.Items;
            definition.JumpPads = review.Proposed.JumpPads;
            definition.NavigationLinks = review.Proposed.NavigationLinks;
            definition.Q3Triggers = review.Proposed.Q3Triggers;
            definition.FormatVersion = review.Proposed.FormatVersion;
        }
    }

    public static string Prepare(MapDefinition definition, string root, Action<RadiantSourceCommitPoint>? fault = null)
    {
        string source = SourcePath(root, definition.MapId), sidecar = SidecarPath(source);
        using var sourceLease = RadiantSourceTransaction.Acquire(source);
        RadiantSourceTransaction.RecoverUnderLease(source, sidecar);
        Manifest? existing = null;
        if (File.Exists(source))
        {
            existing = Read(source);
            if (existing.MapId != definition.MapId)
                throw new InvalidDataException("Radiant source belongs to another map.");
            if (existing.Version == 4) return source;
            // Older workspaces own fewer domains. Upgrade only the exact
            // source and native baseline previously exported.
            if (MapBuildFingerprint.HashFile(source) != existing.SourceHash
                || OwnedHash(definition, existing.Version) != existing.GeometryHash
                || QuakeMapInterchange.ReadIdentity(sidecar).SourceSha256 != existing.SourceHash)
                throw new IOException("Legacy Radiant source cannot be upgraded because source or native geometry changed. "
                    + "Keep both files, reconcile the existing source explicitly, and export a fresh source workspace; "
                    + "opening will not overwrite external edits.");
        }
        var geometry = MapProjectSerializer.Clone(definition);
        geometry.Import = null; geometry.NativeRoom = null; geometry.Collision = null;
        var legacyBrushes = definition.Brushes.ToDictionary(brush => brush.Id);
        foreach (var brush in definition.Brushes) geometry.Geometry.Add(PrimeLegacyBrush.ToMesh(brush, geometry.Materials));
        geometry.Brushes.Clear();
        var reference = RadiantReferenceScene.Prepare(definition, source);
        Guid[] triggerIds = Enumerable.Range(0, definition.Q3Triggers?.Count ?? 0).Select(_ => Guid.NewGuid()).ToArray();
        // Native meshes and parameterized geometry requiring native channels
        // use primeMeshDef; ordinary Quake brushes and patches remain editable.
        QuakeMapInterchange.ExportUnderLease(geometry, source, sidecar,
            hash => ManifestBytes(definition, hash, 4, triggerIds, reference.Identities, reference.DependencyHash),
            fault, editorContainers: true, triggerIds: triggerIds, referenceSource: reference.Source, legacyBrushes: legacyBrushes);
        return source;
    }

    public static RadiantSourceState State(MapDefinition definition, string source)
    {
        try
        {
            var manifest = Read(source);
            if (definition.MapId != manifest.MapId || OwnedHash(definition, manifest.Version) != manifest.GeometryHash
                || !ReferencesFresh(definition, manifest))
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
        string baseline = OwnedHash(current, manifest.Version);
        if (manifest.MapId != current.MapId || manifest.GeometryHash != baseline || !ReferencesFresh(current, manifest))
            throw new IOException("Import conflict: Studio geometry, materials or source-owned gameplay changed since export. "
                + "Keep both sources and reconcile in native Studio or export a new source workspace.");
        string sourceHash = MapBuildFingerprint.HashFile(source);
        if (manifest.Version == 1 && sourceHash != manifest.SourceHash)
            throw new IOException("Legacy Radiant source changed and may have lost comment identities. "
                + "Keep both files, reconcile the existing source explicitly, and export a fresh source workspace "
                + "before applying geometry edits.");
        string sidecarHash = MapBuildFingerprint.HashFile(SidecarPath(source));
        var importResult = QuakeMapInterchange.Import(source, SidecarPath(source),
            requireDurableGeometryContainers: manifest.Version >= 2,
            expectedReferences: manifest.Version >= 4 ? manifest.References!.ToDictionary(reference => reference.Id, reference => reference.PayloadHash) : null);
        var imported = importResult.Definition;
        if (MapBuildFingerprint.HashFile(source) != sourceHash
            || MapBuildFingerprint.HashFile(SidecarPath(source)) != sidecarHash)
            throw new IOException("Radiant source or identity changed while reading. Preview changes again.");
        if (imported.MapId != current.MapId || imported.Name != current.Name)
            throw new InvalidDataException("Source identity or runtime name differs from the open project.");
        if (manifest.Version < 3 && (imported.Spawns.Count > 0 || imported.Items.Count > 0
            || imported.JumpPads.Count > 0 || imported.NavigationLinks.Count > 0 || imported.Q3Triggers?.Count > 0))
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
                if (matches.Length == 1)
                {
                    if (importResult.PrimeMaterialNames.Contains(material.Name)) matches[0].value.TexScale = material.TexScale;
                    return matches[0].index;
                }
                proposed.Materials.Add(material); return proposed.Materials.Count - 1;
            }).ToArray();
            var nativeIds = (manifest.Version < 4 ? current.Brushes.Select(x => x.Id) : Enumerable.Empty<Guid>()).Concat(current.Materials.Select(x => x.Id).Where(id => id != Guid.Empty)).ToHashSet();
            if (manifest.Version < 3)
                nativeIds.UnionWith(current.Spawns.Select(x => x.Id).Concat(current.Items.Select(x => x.Id))
                    .Concat(current.JumpPads.Select(x => x.Id)).Concat(current.NavigationLinks.Select(x => x.Id)));
            if (manifest.Version >= 3 && imported.Spawns.Select(x => x.Id).Concat(imported.Items.Select(x => x.Id))
                .Concat(imported.JumpPads.Select(x => x.Id)).Concat(imported.NavigationLinks.Select(x => x.Id))
                .Concat(importResult.PrimeObjects.TriggerIds).Any(nativeIds.Contains))
                throw new InvalidDataException("Source gameplay ID conflicts with a retained native brush or material.");
            foreach (var geometry in imported.Geometry)
            {
                if (nativeIds.Contains(geometry.Id)) throw new InvalidDataException("Source geometry ID conflicts with a retained native object or material.");
                geometry.Material = mapping[geometry.Material];
                if (geometry is MapMesh mesh)
                    for (int i = 0; i < mesh.FaceMaterials.Count; i++) mesh.FaceMaterials[i] = mapping[mesh.FaceMaterials[i]];
                if (geometry.QuakeFaces != null)
                    foreach (var face in geometry.QuakeFaces) face.Material = mapping[face.Material];
                var original = current.Geometry.FirstOrDefault(x => x.Id == geometry.Id);
                if (original != null && !importResult.PrimeMeshIds.Contains(geometry.Id))
                {
                    geometry.Label = original.Label;
                    if (!importResult.ExplicitLayerIds.Contains(geometry.Id)) geometry.Layer = original.Layer;
                    geometry.Locked = original.Locked; geometry.Terrain = original.Terrain;
                    geometry.Shade = original.Shade;
                }
            }
            proposed.Geometry = imported.Geometry;
            if (manifest.Version >= 4)
            {
                foreach (var brush in imported.Brushes) brush.Material = mapping[brush.Material];
                proposed.Brushes = imported.Brushes;
            }
            if (manifest.Version >= 3)
            {
                proposed.Spawns = imported.Spawns; proposed.Items = imported.Items;
                proposed.JumpPads = imported.JumpPads; proposed.NavigationLinks = imported.NavigationLinks;
                proposed.Q3Triggers = imported.Q3Triggers ?? (current.Q3Triggers == null ? null : new());
                if (proposed.Q3Triggers != null) proposed.FormatVersion = Math.Max(3, proposed.FormatVersion);
            }
        }
        return new(source, sourceHash, sidecarHash, baseline, proposed, current.Geometry.Count, proposed.Geometry.Count)
        {
            WorkspaceVersion = manifest.Version,
            ReferenceDependencyHash = manifest.ReferenceDependencyHash,
            TriggerIds = manifest.Version >= 3 ? importResult.PrimeObjects.TriggerIds.ToArray() : Array.Empty<Guid>(),
            ConvertedGeometryIds = imported.Geometry.OfType<MapMesh>().Where(mesh => (current.Geometry.Any(original => original.Id == mesh.Id && original is not MapMesh) || current.Brushes.Any(original => original.Id == mesh.Id))).Select(mesh => mesh.Id).ToArray(),
            Diagnostics = importResult.Diagnostics
        };
    }

    public static void RequireFresh(MapDefinition current, RadiantGeometryReview review)
    {
        if (current.MapId != review.Proposed.MapId || OwnedHash(current, review.WorkspaceVersion) != review.GeometryBaseline
            || MapBuildFingerprint.HashFile(review.Source) != review.SourceHash
            || MapBuildFingerprint.HashFile(SidecarPath(review.Source)) != review.SidecarHash
            || review.WorkspaceVersion >= 4 && review.ReferenceDependencyHash != RadiantReferenceScene.DependencyHash(current))
            throw new IOException("Source or Studio geometry changed during review. Preview changes again.");
    }

    public static void Accept(MapDefinition current, RadiantGeometryReview review)
    {
        if (MapBuildFingerprint.HashFile(review.Source) != review.SourceHash
            || MapBuildFingerprint.HashFile(SidecarPath(review.Source)) != review.SidecarHash)
            throw new IOException("Source changed after apply; review the newer revision.");
        SaveManifest(review.Source, current, review.SourceHash, review.TriggerIds);
    }
    private static void SaveManifest(string source, MapDefinition definition, string expectedSourceHash, Guid[] triggerIds)
    {
        if (!MapBuildFingerprint.HashFile(source).Equals(expectedSourceHash, StringComparison.OrdinalIgnoreCase))
            throw new IOException("Radiant source changed before its native baseline could be saved. Review the newer revision.");
        var prior = Read(source);
        AtomicFile.Write(ManifestPath(source), ManifestBytes(definition, expectedSourceHash, prior.Version,
            triggerIds, prior.References, prior.Version >= 4 ? RadiantReferenceScene.DependencyHash(definition) : null));
    }
    private static byte[] ManifestBytes(MapDefinition definition, string sourceHash, int version = 4, Guid[]? triggerIds = null,
        RadiantReferenceScene.Identity[]? references = null, string? referenceDependencyHash = null)
    {
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(new Manifest(
            version, definition.MapId, sourceHash.ToLowerInvariant(), OwnedHash(definition, version), definition.Serialize(), triggerIds, references, referenceDependencyHash));
        if (bytes.Length > 64L * 1024 * 1024)
            throw new InvalidDataException("Native Radiant adjunct exceeds its 64 MiB limit. Keep this project in native Studio.");
        return bytes;
    }
    internal static void ValidateManifestBytes(byte[] bytes, Guid mapId, string sourceHash)
    {
        Manifest manifest;
        try { manifest = JsonSerializer.Deserialize<Manifest>(bytes)
            ?? throw new InvalidDataException("Invalid native source recovery metadata."); }
        catch (JsonException ex) { throw new InvalidDataException("Invalid native source recovery metadata.", ex); }
        if (manifest.Version is not (1 or 2 or 3 or 4) || manifest.MapId != mapId
            || !string.Equals(manifest.SourceHash, sourceHash, StringComparison.OrdinalIgnoreCase)
            || manifest.GeometryHash is not { Length: 64 } || manifest.NativeProjectJson == null
            || (manifest.Version >= 3 && (manifest.TriggerIds == null || manifest.TriggerIds.Length > 32768
                || manifest.TriggerIds.Any(id => id == Guid.Empty) || manifest.TriggerIds.Distinct().Count() != manifest.TriggerIds.Length)))
            throw new InvalidDataException("Native source recovery ownership or fingerprint differs.");
        ValidateReferences(manifest);
    }
    private static void ValidateReferences(Manifest manifest)
    {
        if (manifest.Version < 4) return;
        if (manifest.References == null || manifest.References.Length > 4096
            || manifest.References.Any(reference => reference == null || reference.Id == Guid.Empty
                || reference.PayloadHash is not { Length: 64 } || reference.PayloadHash.Any(c => !Uri.IsHexDigit(c)))
            || manifest.References.Select(reference => reference.Id).Distinct().Count() != manifest.References.Length
            || manifest.ReferenceDependencyHash is not { Length: 64 }
            || manifest.ReferenceDependencyHash.Any(c => !Uri.IsHexDigit(c)))
            throw new InvalidDataException("Invalid retained architecture reference ownership.");
    }
    private static Manifest Read(string source)
    {
        string path = ManifestPath(source);
        if (!File.Exists(path) || new FileInfo(path).Length > 64L * 1024 * 1024)
            throw new InvalidDataException("Missing or oversized native Radiant adjunct. Keep source and import explicitly.");
        var result = JsonSerializer.Deserialize<Manifest>(File.ReadAllBytes(path))
            ?? throw new InvalidDataException("Invalid native Radiant adjunct.");
        if (result.Version is not (1 or 2 or 3 or 4) || result.MapId == Guid.Empty)
            throw new InvalidDataException("Unsupported native Radiant adjunct.");
        if (result.Version >= 3 && (result.TriggerIds == null || result.TriggerIds.Length > 32768
            || result.TriggerIds.Any(id => id == Guid.Empty) || result.TriggerIds.Distinct().Count() != result.TriggerIds.Length))
            throw new InvalidDataException("Invalid persistent traversal object identities.");
        ValidateReferences(result);
        if (QuakeMapInterchange.ReadIdentity(SidecarPath(source)).MapId != result.MapId)
            throw new InvalidDataException("Radiant adjunct and sidecar ownership differ.");
        return result;
    }
}

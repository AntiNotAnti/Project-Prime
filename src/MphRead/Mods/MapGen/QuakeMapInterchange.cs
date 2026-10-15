using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using MphRead.Entities;

namespace MphRead.Mods.MapGen;

public sealed record QuakeMapDiagnostic(string Code, int Line, string Message, bool Error);
public sealed record QuakeMapImportResult(
    MapDefinition Definition, QuakeMapDiagnostic[] Diagnostics)
{
    public IReadOnlySet<Guid> PrimeMeshIds { get; init; } = new HashSet<Guid>();
    public IReadOnlySet<Guid> ExplicitLayerIds { get; init; } = new HashSet<Guid>();
    public RadiantObjectSnapshot PrimeObjects { get; init; } = new();
    public IReadOnlySet<string> PrimeMaterialNames { get; init; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
}
public sealed record QuakeMapSidecar(
    int SchemaVersion, Guid MapId, string RoomName, float UnitsPerPrimeUnit,
    string SourceSha256);

public sealed class QuakeMapInterchangeException(
    QuakeMapDiagnostic[] diagnostics) : IOException(
        string.Join("; ", diagnostics.Select(x => x.Code + " line "
            + x.Line + ": " + x.Message)))
{
    public QuakeMapDiagnostic[] Diagnostics { get; } = diagnostics;
}

/// <summary>
/// Editable Quake3 brushes and patches plus Prime Radiant's lossless native
/// polygon and gameplay primitives. Generic Quake export retains its classic
/// subset and coordinate conventions; Prime workspaces use durable containers
/// and canonical mesh payloads for native authoring channels.
/// </summary>
public static class QuakeMapInterchange
{
    public const float DefaultUnits = 16f;
    private static readonly Regex PointPattern = new(
        @"\(\s*(?<x>[-+]?(?:\d+(?:\.\d*)?|\.\d+))\s+(?<y>[-+]?(?:\d+(?:\.\d*)?|\.\d+))\s+(?<z>[-+]?(?:\d+(?:\.\d*)?|\.\d+))\s*\)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex EntityPattern = new(
        "^\"(?<key>[^\"]+)\"\\s+\"(?<value>[^\"]*)\"$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private sealed record Face(Vector3[] Points, string Shader, int Line,
        float ShiftU, float ShiftV, float Rotation, float ScaleU, float ScaleV)
    {
        public bool DefaultProjection => ShiftU == 0 && ShiftV == 0
            && Rotation == 0 && ScaleU == 1 && ScaleV == 1;
    }

    public static QuakeMapImportResult Import(string source, string? sidecarPath = null,
        string? roomName = null, float unitsPerPrimeUnit = DefaultUnits,
        bool requireDurableGeometryContainers = false, IReadOnlyDictionary<Guid, string>? expectedReferences = null)
    {
        if (!float.IsFinite(unitsPerPrimeUnit)
            || unitsPerPrimeUnit is < 1f or > 1024f)
            throw new ArgumentOutOfRangeException(nameof(unitsPerPrimeUnit));
        string full = Path.GetFullPath(source);
        if (sidecarPath != null) RadiantSourceTransaction.Recover(full, sidecarPath);
        if (!File.Exists(full) || Path.GetExtension(full).ToLowerInvariant() != ".map"
            || new FileInfo(full).Length is <= 0 or > 64L * 1024 * 1024)
            throw new InvalidDataException("Input must be a bounded editable Quake .map file.");

        var diagnostics = new List<QuakeMapDiagnostic>();
        void Error(string code, int line, string message)
            => diagnostics.Add(new(code, line, message, true));
        string inferred = roomName ?? Path.GetFileNameWithoutExtension(full).ToUpperInvariant();
        QuakeMapSidecar? sidecar = null;
        if (sidecarPath != null && File.Exists(sidecarPath))
        {
            if (new FileInfo(sidecarPath).Length > 65536)
                throw new InvalidDataException("Quake project sidecar metadata is oversized.");
            sidecar = ReadSidecarStrict(File.ReadAllBytes(sidecarPath));
            if (sidecar?.SchemaVersion != 1 || sidecar.MapId == Guid.Empty)
                throw new InvalidDataException("Unsupported or invalid map identity sidecar.");
            inferred = sidecar.RoomName;
            unitsPerPrimeUnit = sidecar.UnitsPerPrimeUnit;
            if (sidecar.SourceSha256 is not { Length: 64 }
                || sidecar.SourceSha256.Any(c => !Uri.IsHexDigit(c)))
                throw new InvalidDataException("Identity sidecar must pin a valid SHA-256 source fingerprint.");
        }
        // Sidecars are untrusted input. Revalidate after applying their scale:
        // otherwise zero, NaN or infinity can enter gameplay coordinates.
        if (!float.IsFinite(unitsPerPrimeUnit) || unitsPerPrimeUnit is < 1f or > 1024f)
            throw new InvalidDataException("Quake map sidecar scale must be finite and between 1 and 1024.");
        MapValidator.RequireRuntimeName(inferred);
        // Two unrelated maps may share a room filename. Never derive their
        // persistent multiplayer identity from that filename alone.
        Guid mapId = sidecar?.MapId ?? Guid.NewGuid();
        if (sidecar == null)
            diagnostics.Add(new("FP-RADIANT-NEW-ID", 0,
                "No project-prime.json sidecar; imported a new map identity. "
                + "Export a sidecar before reimporting edits to preserve it.", false));
        string sourceHash = MapBuildFingerprint.HashFile(full);
        if (sidecar != null
            && !sidecar.SourceSha256.Equals(sourceHash, StringComparison.OrdinalIgnoreCase))
            diagnostics.Add(new("FP-RADIANT-SOURCE-CHANGED", 0,
                "The source .map changed after the sidecar was saved; check entity/brush IDs.",
                false));

        var definition = new MapDefinition
        {
            FormatVersion = 2, MapId = mapId,
            Name = inferred, InGameName = inferred,
            Version = "1", BaseDirectory = Path.GetDirectoryName(full)
        };
        var materials = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var entity = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var faces = new List<Face>();
        Guid brushId = Guid.Empty;
        int depth = 0;
        int entityPrimitiveCount = 0;
        int lineNo = 0, brushIndex = 0, patchIndex = 0, spawnIndex = 0, itemIndex = 0, worldspawnCount = 0;
        var seenObjectIds = new HashSet<Guid>();
        var primeMeshIds = new HashSet<Guid>();
        var referenceIds = new HashSet<Guid>();
        var primeObjects = new RadiantObjectSnapshot();
        var primeMaterialNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var layers = new RadiantLayerInterchange();
        var nativeLayers = new List<(MapGeometry Geometry, int Index)>();
        var explicitLayerIds = new HashSet<Guid>();
        int primitiveLayerIndex = 0;
        bool legacyPrimeWinding = false;
        bool inPatch = false, patchBodyClosed = false;
        bool? patchSolid = null;
        int? patchTessellation = null;
        var patchBody = new List<string>();
        bool inPrimeMesh = false, primeMeshBodyClosed = false;
        var primeMeshBody = new List<string>();

        bool ReferenceContainer() => entity.GetValueOrDefault("prime_reference", "") == "1";

        bool GeometryContainer() => entity.GetValueOrDefault("classname", "")
            .Equals("func_group", StringComparison.OrdinalIgnoreCase);

        void OpenPrimitive()
        {
            primitiveLayerIndex = layers.CurrentIndex;
            entityPrimitiveCount++;
            if (entity.GetValueOrDefault("classname", "").Equals("worldspawn", StringComparison.OrdinalIgnoreCase))
            {
                if (requireDurableGeometryContainers)
                    Error("FP-RADIANT-DURABLE-GEOMETRY", lineNo,
                        "This source workspace requires every brush and patch inside a marked Prime geometry container with a durable prime_id. "
                        + "Save it with Project Prime Radiant before reviewing changes; comment-only identities are not durable.");
                return;
            }
            if (!GeometryContainer())
            {
                Error("FP-RADIANT-BRUSH-ENTITY", lineNo,
                    "Only worldspawn geometry or marked Prime geometry containers are supported.");
                return;
            }
            foreach (string key in entity.Keys)
                if (key is not ("classname" or "prime_geometry" or "prime_id"
                    or "prime_patch_solid" or "prime_patch_tessellation" or "prime_reference"))
                    Error("FP-RADIANT-CONTAINER", lineNo, "Unsupported geometry container key: " + key);
            if (entity.GetValueOrDefault("prime_geometry") != "1" || entityPrimitiveCount != 1)
                Error("FP-RADIANT-CONTAINER", lineNo,
                    "A marked Prime geometry container must contain exactly one primitive.");
            if (entity.TryGetValue("prime_reference", out var referenceMarker) && referenceMarker != "1")
                Error("FP-RADIANT-REFERENCE", lineNo, "Invalid retained architecture reference marker.");
            if (!Guid.TryParse(entity.GetValueOrDefault("prime_id"), out brushId) || brushId == Guid.Empty)
                Error("FP-RADIANT-ID", lineNo, "Geometry container prime_id must be a nonempty GUID.");
            if (entity.TryGetValue("prime_patch_solid", out string? solidText))
            {
                if (!bool.TryParse(solidText, out bool solid))
                    Error("FP-RADIANT-PATCH", lineNo, "Invalid container patch collision metadata.");
                else patchSolid = solid;
            }
            if (entity.TryGetValue("prime_patch_tessellation", out string? tessellationText))
            {
                if (!int.TryParse(tessellationText, NumberStyles.None, CultureInfo.InvariantCulture,
                    out int level) || level is < 1 or > BezierPatchCompiler.MaximumTessellation)
                    Error("FP-RADIANT-PATCH", lineNo, "Invalid container patch tessellation metadata.");
                else patchTessellation = level;
            }
        }

        int Material(string shaderName)
        {
            if (RadiantReferenceScene.IsReferenceShader(shaderName))
                throw new InvalidDataException("Reserved source reference shaders cannot become authored material bindings.");
            if (!materials.TryGetValue(shaderName, out int slot))
            {
                slot = definition.Materials.Count;
                materials.Add(shaderName, slot);
                Guid materialId = GuidFrom("prime-material:" + shaderName.ToLowerInvariant());
                if (!seenObjectIds.Add(materialId))
                    Error("FP-RADIANT-ID", lineNo, "Material identity conflicts with a source object.");
                definition.Materials.Add(new MapMaterial
                {
                    Id = materialId,
                    Name = shaderName, SourceMaterial = 2, TexScale = 16f
                });
            }
            return slot;
        }

        void ClosePatch()
        {
            if (ReferenceContainer()) { Error("FP-RADIANT-REFERENCE", lineNo, "References must remain native mesh primitives."); return; }
            if (patchIndex >= 32)
            {
                Error("FP-RADIANT-PATCH", lineNo, "Map exceeds 32 editable Bezier patches.");
                return;
            }
            if (!inPatch || !patchBodyClosed)
            {
                Error("FP-RADIANT-PATCH", lineNo, "Incomplete patchDef2 data block.");
                return;
            }
            QuakePatchDef2.Parsed parsed;
            try { parsed = QuakePatchDef2.Parse(patchBody, unitsPerPrimeUnit); }
            catch (InvalidDataException ex)
            {
                Error("FP-RADIANT-PATCH", lineNo, ex.Message);
                return;
            }
            if (!SafeShader(parsed.Shader))
            {
                Error("FP-RADIANT-PATCH", lineNo, "Invalid Quake patch shader name.");
                return;
            }
            MapBezierPatch patch = parsed.Geometry;
            patch.Material = Material(parsed.Shader);
            patch.Tessellation = patchTessellation ?? 3;
            patch.Id = brushId != Guid.Empty ? brushId
                : GuidFrom("prime-patch:" + mapId + ":" + patchIndex);
            patch.Label = "Quake patch " + (patchIndex + 1);
            patch.Solid = patchSolid ?? true;
            if (patchSolid == null)
                diagnostics.Add(new("FP-RADIANT-PATCH-COLLISION", lineNo,
                    "Patch collision defaults to solid; verify the original Quake shader "
                    + "and save an explicit PRIME_PATCH_SOLID flag before publishing.", false));
            if (!seenObjectIds.Add(patch.Id))
            {
                Error("FP-RADIANT-ID", lineNo, "Duplicate patch/brush/entity identity.");
                return;
            }
            try { GeometryCompiler.Compile(patch, definition.Materials[patch.Material].TexScale); }
            catch (MapAuthoringException ex)
            {
                Error(ex.Code, lineNo, ex.Message);
                return;
            }
            definition.Geometry.Add(patch);
            nativeLayers.Add((patch, primitiveLayerIndex));
            patchIndex++;
            patchBody.Clear();
            patchBodyClosed = inPatch = false;
            patchSolid = null;
            patchTessellation = null;
            brushId = Guid.Empty;
        }

        void ClosePrimeMesh()
        {
            if (!inPrimeMesh || !primeMeshBodyClosed || !GeometryContainer())
            {
                Error("FP-RADIANT-MESH", lineNo, "primeMeshDef requires a complete marked geometry container.");
                return;
            }
            try
            {
                var parsed = PrimeMeshInterchange.Parse(primeMeshBody, unitsPerPrimeUnit, brushId);
                if (ReferenceContainer() || parsed.Reference || expectedReferences?.ContainsKey(brushId) == true)
                {
                    if (!ReferenceContainer() || !parsed.Reference || expectedReferences == null
                        || !expectedReferences.TryGetValue(brushId, out string? expected)
                        || !string.Equals(expected, RadiantReferenceScene.PayloadHash(parsed, unitsPerPrimeUnit), StringComparison.OrdinalIgnoreCase)
                        || !referenceIds.Add(brushId) || !seenObjectIds.Add(brushId))
                        throw new InvalidDataException("Retained architecture reference was changed, duplicated or lacks its owned identity.");
                    return;
                }
                var mapping = new int[parsed.Materials.Length];
                for (int i = 0; i < mapping.Length; i++)
                {
                    var value = parsed.Materials[i];
                    bool existing = materials.ContainsKey(value.Name);
                    int slot = Material(value.Name);
                    if (existing && primeMaterialNames.Contains(value.Name) && definition.Materials[slot].TexScale != value.TexScale)
                        throw new InvalidDataException("Conflicting Prime mesh texel scale for material " + value.Name + ".");
                    definition.Materials[slot].TexScale = value.TexScale;
                    primeMaterialNames.Add(value.Name);
                    mapping[i] = slot;
                }
                var geometry = parsed.Geometry;
                geometry.Material = mapping[geometry.Material];
                if (geometry is MapMesh mesh)
                    for (int i = 0; i < mesh.FaceMaterials.Count; i++) mesh.FaceMaterials[i] = mapping[mesh.FaceMaterials[i]];
                if (geometry.QuakeFaces != null)
                    foreach (var face in geometry.QuakeFaces) face.Material = mapping[face.Material];
                if (!seenObjectIds.Add(geometry.Id))
                    throw new InvalidDataException("Duplicate Prime mesh/container identity.");
                if (parsed.LegacyBrush is { } legacy)
                {
                    legacy.Material = mapping[legacy.Material];
                    definition.Brushes.Add(legacy);
                }
                else
                {
                    definition.Geometry.Add(geometry);
                    nativeLayers.Add((geometry, primitiveLayerIndex));
                }
                primeMeshIds.Add(geometry.Id);
                if (parsed.ConvertedToMesh)
                    diagnostics.Add(new("FP-RADIANT-MESH-CONVERSION", lineNo,
                        "Edited parameterized geometry or legacy brush is now an explicit MapMesh; review this source kind change before applying.", false));
            }
            catch (InvalidDataException ex) { Error("FP-RADIANT-MESH", lineNo, ex.Message); }
            finally
            {
                primeMeshBody.Clear();
                primeMeshBodyClosed = inPrimeMesh = false;
                brushId = Guid.Empty;
            }
        }

        void CloseBrush()
        {
            if (ReferenceContainer()) { Error("FP-RADIANT-REFERENCE", lineNo, "References must remain native mesh primitives."); return; }
            if (faces.Count is < 4 or > 32)
            {
                Error("FP-RADIANT-BRUSH", lineNo,
                    "Editable Quake convex brushes require between 4 and 32 complete planes.");
                faces.Clear();
                brushId = Guid.Empty;
                return;
            }
            string shader = faces[0].Shader;
            bool needsQuakeProjection = faces.Any(face =>
                !string.Equals(face.Shader, shader, StringComparison.OrdinalIgnoreCase)
                || !face.DefaultProjection);
            if (diagnostics.Any(d => d.Error)) return;

            MapGeometry geometry;
            try
            {
                geometry = QuakeConvexHull.FromQuakePlanes(
                    faces.Select(f => f.Points).ToArray(), unitsPerPrimeUnit, legacyPrimeWinding);
            }
            catch (InvalidOperationException ex)
            {
                Error("FP-RADIANT-CONVEX", faces[0].Line, ex.Message);
                return;
            }
            int material = Material(shader);
            if (needsQuakeProjection)
            {
                var projected = new MapQuakeFace?[faces.Count];
                IReadOnlyList<BuiltFace>? boxFaces = null;
                if (geometry is MapBox)
                    boxFaces = GeometryCompiler.Compile(geometry, 16f);
                for (int faceIndex = 0; faceIndex < faces.Count; faceIndex++)
                {
                    Face face = faces[faceIndex];
                    int targetIndex = faceIndex;
                    if (boxFaces != null)
                    {
                        Vector3 normal = Vector3.Normalize(legacyPrimeWinding
                            ? Vector3.Cross(face.Points[1] - face.Points[0], face.Points[2] - face.Points[0])
                            : Vector3.Cross(face.Points[2] - face.Points[0], face.Points[1] - face.Points[0]));
                        Vector3 converted = new(normal.X, normal.Z, -normal.Y);
                        targetIndex = -1;
                        for (int i = 0; i < boxFaces.Count; i++)
                        {
                            var n = boxFaces[i].Normal;
                            if (Vector3.Dot(converted, new Vector3(n.X, n.Y, n.Z)) > 0.9999f)
                            {
                                targetIndex = i;
                                break;
                            }
                        }
                        if (targetIndex < 0 || projected[targetIndex] != null)
                        {
                            Error("FP-RADIANT-MATERIAL", face.Line,
                                "Cannot assign Quake shader projection to a unique box face.");
                            return;
                        }
                    }
                    projected[targetIndex] = new MapQuakeFace
                    {
                        Material = Material(face.Shader),
                        ShiftU = face.ShiftU,
                        ShiftV = face.ShiftV,
                        Rotation = face.Rotation,
                        ScaleU = face.ScaleU,
                        ScaleV = face.ScaleV,
                        UnitsPerPrimeUnit = unitsPerPrimeUnit
                    };
                }
                if (projected.Any(q => q == null))
                {
                    Error("FP-RADIANT-MATERIAL", lineNo,
                        "Incomplete authored per-face texture mapping.");
                    return;
                }
                geometry.QuakeFaces = projected.Select(q => q!).ToList();
            }
            Guid id = brushId != Guid.Empty ? brushId
                : GuidFrom("prime-brush:" + mapId + ":" + brushIndex + ":"
                    + string.Join("|", faces.Select(f =>
                        string.Join(",", f.Points.SelectMany(p => new[] { p.X, p.Y, p.Z })
                            .Select(v => v.ToString("R", CultureInfo.InvariantCulture))))));
            if (!seenObjectIds.Add(id))
            {
                Error("FP-RADIANT-ID", lineNo,
                    "Duplicate brush identity; map authoring IDs must be unique.");
                return;
            }
            geometry.Id = id;
            geometry.Label = "Quake brush " + (brushIndex + 1);
            geometry.Material = material;
            // A solved hull still needs to satisfy Prime's native face and UV
            // budgets before it becomes an editable authoring document.
            try { GeometryCompiler.Compile(geometry, definition.Materials[material].TexScale); }
            catch (MapAuthoringException ex)
            {
                Error(ex.Code, lineNo, ex.Message);
                return;
            }
            definition.Geometry.Add(geometry);
            nativeLayers.Add((geometry, primitiveLayerIndex));
            brushIndex++;
            brushId = Guid.Empty;
            faces.Clear();
        }

        void CloseEntity()
        {
            string type = entity.GetValueOrDefault("classname") ?? "";
            if (GeometryContainer())
            {
                if (entity.GetValueOrDefault("prime_geometry") != "1" || entityPrimitiveCount != 1)
                    Error("FP-RADIANT-CONTAINER", lineNo,
                        "A marked Prime geometry container must contain exactly one primitive.");
            }
            else if (type.Equals("worldspawn", StringComparison.OrdinalIgnoreCase))
            {
                worldspawnCount++;
                if (worldspawnCount != 1)
                    Error("FP-RADIANT-WORLDSPAWN", lineNo, "Exactly one worldspawn is required.");
                // The native MapDefinition currently has no source-key metadata.
                // Even apparently cosmetic keys cannot be discarded silently.
                foreach (string key in entity.Keys)
                    if (!key.Equals("classname", StringComparison.OrdinalIgnoreCase))
                        Error("FP-RADIANT-WORLDSPAWN", lineNo,
                            "Unrepresented worldspawn key " + key + " requires explicit preservation.");
            }
            else if (RadiantObjectInterchange.IsPrimeClass(type))
            {
                if (entityPrimitiveCount != 0)
                    Error("FP-RADIANT-OBJECT", lineNo, "Prime gameplay point entities cannot contain geometry primitives.");
                else if (!RadiantObjectInterchange.TryParseEntity(entity, unitsPerPrimeUnit, out var record, out string? error) || record == null)
                    Error("FP-RADIANT-OBJECT", lineNo, error ?? "Malformed Prime gameplay entity.");
                else
                {
                    try { primeObjects.Add(record, seenObjectIds); }
                    catch (InvalidDataException ex) { Error("FP-RADIANT-OBJECT", lineNo, ex.Message); }
                }
            }
            else if (type.Equals("info_player_deathmatch", StringComparison.OrdinalIgnoreCase))
            {
                if (!Q3Gameplay.TryVector(entity.GetValueOrDefault("origin"), out var xyz))
                { Error("FP-RADIANT-SPAWN", lineNo, "Spawn requires a valid Quake origin."); return; }
                foreach (string key in entity.Keys)
                    if (key is not ("classname" or "origin" or "angle" or "prime_id"))
                        Error("FP-RADIANT-ENTITY", lineNo, "Unsupported spawn key: " + key);
                float angle = 0;
                if (entity.TryGetValue("angle", out string? value)
                    && (!float.TryParse(value, NumberStyles.Float,
                        CultureInfo.InvariantCulture, out angle) || !float.IsFinite(angle)))
                    Error("FP-RADIANT-SPAWN", lineNo, "Invalid spawn heading.");
                Guid id;
                if (entity.TryGetValue("prime_id", out var idText))
                {
                    if (!Guid.TryParse(idText, out id) || id == Guid.Empty)
                    {
                        Error("FP-RADIANT-ID", lineNo, "Spawn prime_id must be a nonempty GUID.");
                        return;
                    }
                }
                else id = GuidFrom("prime-spawn:" + mapId + ":" + spawnIndex);
                if (!seenObjectIds.Add(id))
                {
                    Error("FP-RADIANT-ID", lineNo, "Duplicate spawn prime_id is not allowed.");
                    return;
                }
                definition.Spawns.Add(new MapSpawn
                {
                    Id = id, Position = new[]
                    {
                        xyz[0] / unitsPerPrimeUnit,
                        (xyz[2] - 24f) / unitsPerPrimeUnit,
                        -xyz[1] / unitsPerPrimeUnit
                    }, Yaw = (90f + angle % 360f + 360f) % 360f
                });
                spawnIndex++;
            }
            else if (Q3Gameplay.MapPickupType(type) is ItemType pickupType
                && pickupType != ItemType.None)
            {
                if (!string.Equals(Q3Gameplay.CanonicalQuakePickup(pickupType.ToString()),
                    type, StringComparison.OrdinalIgnoreCase))
                    Error("FP-RADIANT-ITEM", lineNo,
                        "Noncanonical Quake pickup alias would lose its source classname on export.");
                if (!Q3Gameplay.TryVector(entity.GetValueOrDefault("origin"), out var xyz))
                {
                    Error("FP-RADIANT-ITEM", lineNo, "Pickup requires finite Quake origin.");
                    return;
                }
                foreach (string key in entity.Keys)
                    if (key is not ("classname" or "origin" or "prime_id"
                        or "prime_spawn_interval" or "prime_has_base"))
                        Error("FP-RADIANT-ITEM", lineNo,
                            "Unsupported pickup metadata: " + key);
                Guid id;
                if (entity.TryGetValue("prime_id", out var idText))
                {
                    if (!Guid.TryParse(idText, out id) || id == Guid.Empty)
                    {
                        Error("FP-RADIANT-ID", lineNo, "Pickup prime_id must be a nonempty GUID.");
                        return;
                    }
                }
                else id = GuidFrom("prime-item:" + mapId + ":" + itemIndex);
                if (!seenObjectIds.Add(id))
                {
                    Error("FP-RADIANT-ID", lineNo,
                        "Duplicate entity identity across brush, spawn or pickup.");
                    return;
                }
                ushort interval = 300;
                bool hasBase = true;
                if (entity.TryGetValue("prime_spawn_interval", out var intervalText)
                    && !ushort.TryParse(intervalText, NumberStyles.None,
                        CultureInfo.InvariantCulture, out interval))
                    Error("FP-RADIANT-ITEM", lineNo, "Invalid pickup respawn interval.");
                if (entity.TryGetValue("prime_has_base", out var baseText)
                    && !bool.TryParse(baseText, out hasBase))
                    Error("FP-RADIANT-ITEM", lineNo, "Invalid pickup base flag.");
                definition.Items.Add(new MapItem
                {
                    Id = id,
                    Position = new[]
                    {
                        xyz[0] / unitsPerPrimeUnit, xyz[2] / unitsPerPrimeUnit,
                        -xyz[1] / unitsPerPrimeUnit
                    },
                    Type = pickupType.ToString(),
                    SpawnInterval = interval,
                    HasBase = hasBase
                });
                itemIndex++;
            }
            else
                Error("FP-RADIANT-ENTITY", lineNo,
                    "Unsupported Quake entity: " + type + ". No gameplay entity was silently dropped.");
            entity.Clear();
            entityPrimitiveCount = 0;
        }

        foreach (string raw in File.ReadLines(full))
        {
            lineNo++;
            if (lineNo > 2000000)
                throw new InvalidDataException("Quake map contains too many lines.");
            if (raw.Length > 4096)
                throw new InvalidDataException("Quake map has an oversized source line.");
            string line = raw.Trim();
            // Native layer directives are encoded as special comments, so
            // consume them before ordinary source comments are skipped.
            try { if (layers.Read(line, depth)) continue; }
            catch (InvalidDataException ex)
            {
                Error("FP-RADIANT-LAYER", lineNo, ex.Message);
                break;
            }
            // The exact v1 exporter header is a format marker, not permission
            // to guess at malformed third-party planes. Real Quake sources
            // and every newly exported source use Quake's PlaneFromPoints.
            if (line == "// Project Prime original Quake3 .map brush authoring source" && depth == 0)
            {
                if (worldspawnCount != 0)
                    Error("FP-RADIANT-WINDING", lineNo, "Legacy winding marker must precede map entities.");
                else
                {
                    legacyPrimeWinding = true;
                    diagnostics.Add(new("FP-RADIANT-LEGACY-WINDING", lineNo,
                        "Historical Prime source uses legacy plane winding. Import and export a new .map before using a real Quake compiler.", false));
                }
                continue;
            }
            if (line.StartsWith("// PRIME_PLANE_WINDING=", StringComparison.Ordinal) && depth == 0)
            {
                if (worldspawnCount != 0 || line != "// PRIME_PLANE_WINDING=quake3")
                    Error("FP-RADIANT-WINDING", lineNo, "Unsupported or misplaced source plane winding marker.");
                else legacyPrimeWinding = false;
                continue;
            }
            if (line.StartsWith("// PRIME_ID=", StringComparison.OrdinalIgnoreCase)
                && depth == 2)
            {
                if (GeometryContainer())
                    Error("FP-RADIANT-CONTAINER", lineNo, "Container identity must use its prime_id property.");
                if (!Guid.TryParse(line[12..], out brushId) || brushId == Guid.Empty)
                    Error("FP-RADIANT-ID", lineNo, "Invalid or empty geometry identity marker.");
                continue;
            }
            if (line.StartsWith("// PRIME_PATCH_SOLID=", StringComparison.OrdinalIgnoreCase)
                && depth == 2)
            {
                if (GeometryContainer())
                    Error("FP-RADIANT-CONTAINER", lineNo, "Container patch flags must use entity properties.");
                if (inPatch || patchSolid != null
                    || !bool.TryParse(line["// PRIME_PATCH_SOLID=".Length..], out bool solid))
                    Error("FP-RADIANT-PATCH", lineNo, "Invalid/duplicate patch collision metadata.");
                else patchSolid = solid;
                continue;
            }
            if (line.StartsWith("// PRIME_PATCH_TESSELLATION=", StringComparison.OrdinalIgnoreCase)
                && depth == 2)
            {
                if (GeometryContainer())
                    Error("FP-RADIANT-CONTAINER", lineNo, "Container patch flags must use entity properties.");
                if (inPatch || patchTessellation.HasValue
                    || !int.TryParse(line["// PRIME_PATCH_TESSELLATION=".Length..],
                        NumberStyles.None, CultureInfo.InvariantCulture, out int level)
                    || level is < 1 or > BezierPatchCompiler.MaximumTessellation)
                    Error("FP-RADIANT-PATCH", lineNo,
                        "Invalid or duplicate patch tessellation metadata.");
                else patchTessellation = level;
                continue;
            }
            if (line.Length == 0 || line.StartsWith("//", StringComparison.Ordinal))
                continue;
            if (depth == 2 && line == "primeMeshDef")
            {
                if (!GeometryContainer() || inPrimeMesh || inPatch || faces.Count > 0 || primeMeshBodyClosed
                    || patchSolid != null || patchTessellation != null)
                    Error("FP-RADIANT-MESH", lineNo, "primeMeshDef requires one marked container without brush or patch metadata.");
                else inPrimeMesh = true;
                continue;
            }
            if (depth == 2 && line == "patchDef2")
            {
                if (GeometryContainer() && (patchSolid == null || patchTessellation == null))
                    Error("FP-RADIANT-PATCH", lineNo, "Marked patches require explicit collision and tessellation properties.");
                if (inPatch || inPrimeMesh || faces.Count > 0 || patchBodyClosed)
                    Error("FP-RADIANT-PATCH", lineNo,
                        "Cannot mix a quadratic patch and brush planes in one source object.");
                else inPatch = true;
                continue;
            }
            if (depth == 2 && line is "patchDef3" or "brushDef" or "brushDef3")
            {
                Error("FP-RADIANT-PATCH", lineNo,
                    "Unsupported patchDef3 or brushDef primitive; source is not silently rewritten.");
                break;
            }
            if (line == "{")
            {
                if (depth == 0)
                {
                    try { layers.Seal(); }
                    catch (InvalidDataException ex)
                    {
                        Error("FP-RADIANT-LAYER", lineNo, ex.Message);
                        break;
                    }
                }
                depth++;
                if (depth > 3 || (depth == 3 && !inPatch && !inPrimeMesh))
                {
                    Error("FP-RADIANT-PATCH", lineNo,
                        "Unsupported nested patch, brushDef or source object.");
                    break;
                }
                if (depth == 2) OpenPrimitive();
                continue;
            }
            if (line == "}")
            {
                if (depth == 3 && inPrimeMesh)
                    primeMeshBodyClosed = true;
                else if (depth == 3 && inPatch)
                    patchBodyClosed = true;
                else if (depth == 2)
                {
                    if (inPrimeMesh) ClosePrimeMesh();
                    else if (inPatch) ClosePatch();
                    else CloseBrush();
                    if (!inPatch) patchSolid = null;
                }
                else if (depth == 1) CloseEntity();
                else Error("FP-RADIANT-BRACE", lineNo, "Unexpected closing brace.");
                depth--;
                continue;
            }
            if (inPrimeMesh)
            {
                if (depth != 3 || primeMeshBodyClosed)
                    Error("FP-RADIANT-MESH", lineNo, "primeMeshDef data must be inside its nested source block.");
                else primeMeshBody.Add(line);
                continue;
            }
            if (inPatch)
            {
                if (depth != 3 || patchBodyClosed)
                    Error("FP-RADIANT-PATCH", lineNo,
                        "patchDef2 data must be inside its nested source block.");
                else patchBody.Add(line);
                continue;
            }
            if ((patchSolid != null || patchTessellation != null) && depth == 2)
            {
                Error("FP-RADIANT-PATCH", lineNo,
                    "Patch metadata must precede a patchDef2, not brush planes.");
                continue;
            }
            if (depth == 1)
            {
                if (entityPrimitiveCount > 0)
                    Error("FP-RADIANT-CONTAINER", lineNo, "Entity properties must precede all primitives.");
                Match kv = EntityPattern.Match(line);
                if (kv.Success)
                {
                    if (!entity.TryAdd(kv.Groups["key"].Value, kv.Groups["value"].Value))
                        Error("FP-RADIANT-ENTITY", lineNo,
                            "Duplicate entity key; silently replacing metadata is unsafe.");
                }
                else Error("FP-RADIANT-ENTITY", lineNo,
                    "Unsupported entity syntax or escaped key/value.");
                continue;
            }
            if (depth == 2)
            {
                MatchCollection matches = PointPattern.Matches(line);
                if (matches.Count != 3
                    || (matches.Count == 3 && (matches[0].Index != 0
                        || !string.IsNullOrWhiteSpace(line[
                            (matches[0].Index + matches[0].Length)..matches[1].Index])
                        || !string.IsNullOrWhiteSpace(line[
                            (matches[1].Index + matches[1].Length)..matches[2].Index]))))
                {
                    Error("FP-RADIANT-FACE", lineNo,
                        "Expected exactly three Quake plane points without unsupported prefixes.");
                    continue;
                }
                var points = new Vector3[3];
                bool invalidCoordinate = false;
                for (int i = 0; i < 3; i++)
                {
                    Match m = matches[i];
                    if (!float.TryParse(m.Groups["x"].Value, NumberStyles.Float,
                            CultureInfo.InvariantCulture, out float x)
                        || !float.TryParse(m.Groups["y"].Value, NumberStyles.Float,
                            CultureInfo.InvariantCulture, out float y)
                        || !float.TryParse(m.Groups["z"].Value, NumberStyles.Float,
                            CultureInfo.InvariantCulture, out float z)
                        || !float.IsFinite(x) || !float.IsFinite(y) || !float.IsFinite(z))
                    {
                        Error("FP-RADIANT-PLANE", lineNo,
                            "Brush plane has an invalid or nonfinite coordinate.");
                        invalidCoordinate = true;
                        break;
                    }
                    points[i] = new Vector3(x, y, z);
                }
                if (invalidCoordinate) continue;
                string tail = line[(matches[2].Index + matches[2].Length)..].Trim();
                string[] args = tail.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
                if (args.Length is not (6 or 9) || !SafeShader(args[0]))
                {
                    Error("FP-RADIANT-UV", lineNo, "Unsupported surface shader/UV syntax.");
                    continue;
                }
                var tex = new float[5];
                bool wellFormed = true;
                for (int n = 0; n < 5; n++)
                    if (!float.TryParse(args[n + 1], NumberStyles.Float,
                            CultureInfo.InvariantCulture, out tex[n])
                        || !float.IsFinite(tex[n]))
                        wellFormed = false;
                bool flagsDefault = args.Length == 6
                    || args.Skip(6).All(value => int.TryParse(value,
                        NumberStyles.Integer, CultureInfo.InvariantCulture, out int v) && v == 0);
                var verified = new MapQuakeFace
                {
                    ShiftU = tex[0], ShiftV = tex[1],
                    Rotation = tex[2], ScaleU = tex[3], ScaleV = tex[4],
                    UnitsPerPrimeUnit = unitsPerPrimeUnit
                };
                if (!wellFormed || !verified.Valid || !flagsDefault)
                {
                    Error("FP-RADIANT-UV", lineNo,
                        "Quake face projection is malformed, over budget or has unsupported surface flags.");
                    continue;
                }
                if (faces.Count >= 32)
                    Error("FP-RADIANT-BRUSH", lineNo,
                        "Convex brush exceeds the 32-plane authoring limit.");
                else faces.Add(new Face(points, args[0], lineNo,
                    tex[0], tex[1], tex[2], tex[3], tex[4]));
                continue;
            }
            Error("FP-RADIANT-SYNTAX", lineNo,
                "Unsupported text outside entities or brush geometry.");
        }
        if (depth != 0)
            Error("FP-RADIANT-BRACE", lineNo, "Unclosed Quake map entity or brush.");
        try
        {
            layers.Seal();
            if (layers.HasCanonicalAssignments)
                foreach (var assigned in nativeLayers)
                {
                    // Verify Prime recovery/reference payloads before native
                    // assignments override stale canonical authoring metadata.
                    assigned.Geometry.Layer = layers.Path(assigned.Index);
                    explicitLayerIds.Add(assigned.Geometry.Id);
                }
        }
        catch (InvalidDataException ex) { Error("FP-RADIANT-LAYER", lineNo, ex.Message); }
        if (worldspawnCount != 1)
            Error("FP-RADIANT-WORLDSPAWN", lineNo, "Expected exactly one worldspawn.");
        if (MapBuildFingerprint.HashFile(full) != sourceHash)
            Error("FP-RADIANT-SOURCE-RACE", lineNo,
                "The .map source changed while being read; retry from a frozen source.");
        if (!definition.Geometry.Any() && !definition.Brushes.Any() && referenceIds.Count == 0
            && !requireDurableGeometryContainers)
            Error("FP-RADIANT-GEOMETRY", lineNo, "No supported editable geometry found.");

        definition.Spawns.AddRange(primeObjects.Spawns);
        definition.Items.AddRange(primeObjects.Items);
        definition.JumpPads.AddRange(primeObjects.JumpPads);
        definition.NavigationLinks.AddRange(primeObjects.NavigationLinks);
        if (primeObjects.Triggers.Count > 0)
        {
            definition.Q3Triggers = primeObjects.Triggers.ToList();
            definition.FormatVersion = Math.Max(3, definition.FormatVersion);
        }

        // Quake maps can exceed the +/-128 Prime-unit geometry range at the
        // default ScaleFactor 4. Choose the smallest supported fixed-point
        // scale that contains every authored object, without altering
        // gameplay coordinates or requiring a destructive source edit.
        float furthest = 0;
        void Measure(IEnumerable<float> coordinates)
        {
            foreach (float value in coordinates)
            {
                if (!float.IsFinite(value))
                    Error("FP-RADIANT-RANGE", lineNo, "Imported geometry or entity has nonfinite coordinates.");
                else furthest = MathF.Max(furthest, MathF.Abs(value));
            }
        }
        foreach (MapGeometry geometry in definition.Geometry)
        {
            if (primeMeshIds.Contains(geometry.Id) && geometry is not MapMesh)
            {
                foreach (var face in GeometryCompiler.Compile(geometry, definition.Materials[geometry.Material].TexScale))
                    foreach (var point in face.Points) Measure(new[] { point.X, point.Y, point.Z });
            }
            else if (geometry is MapBox box)
                Measure(Enumerable.Range(0, 3).Select(i =>
                    Math.Abs(box.Transform.Position[i]) + box.Transform.Scale[i] / 2));
            else if (geometry is MapConvexBrush convex)
                foreach (float[] vertex in convex.Vertices) Measure(vertex);
            else if (geometry is MapBezierPatch patch)
                foreach (float[] control in patch.ControlPoints) Measure(control.Take(3));
            else if (geometry is MapMesh mesh)
                for (int i = 0; i < mesh.Vertices.Count; i++)
                {
                    Vector3 point = MphRead.Mods.MapEditor.MapMeshEditing.VertexWorld(mesh, i);
                    Measure(new[] { point.X, point.Y, point.Z });
                }
            else if (geometry is MapWedge or MapPrism)
                foreach (var face in GeometryCompiler.Compile(geometry, definition.Materials[geometry.Material].TexScale))
                    foreach (var point in face.Points) Measure(new[] { point.X, point.Y, point.Z });
        }
        foreach (var brush in definition.Brushes) { Measure(brush.Min); Measure(brush.Max); }
        foreach (MapSpawn spawn in definition.Spawns) Measure(spawn.Position);
        foreach (MapItem item in definition.Items) Measure(item.Position);
        foreach (MapJumpPad pad in definition.JumpPads) { Measure(pad.Position); if (pad.Target != null) Measure(pad.Target); }
        foreach (MapNavigationLink link in definition.NavigationLinks) { Measure(link.From); Measure(link.To); }
        foreach (MapQ3Trigger trigger in definition.Q3Triggers ?? Enumerable.Empty<MapQ3Trigger>())
        { Measure(trigger.Min); Measure(trigger.Max); Measure(trigger.Destination); }
        while (definition.ScaleFactor < 16
            && furthest >= 8 * MathF.Pow(2, definition.ScaleFactor))
            definition.ScaleFactor++;
        if (furthest >= 8 * MathF.Pow(2, definition.ScaleFactor)
            || furthest >= 524288)
            Error("FP-RADIANT-RANGE", lineNo,
                "Imported coordinates exceed the supported native fixed-point map range.");

        if (expectedReferences != null && !referenceIds.SetEquals(expectedReferences.Keys))
            Error("FP-RADIANT-REFERENCE", 0, "Retained architecture references are missing or have unrecognized identities.");
        if (diagnostics.Any(d => d.Error))
            throw new QuakeMapInterchangeException(diagnostics.ToArray());
        return new QuakeMapImportResult(definition, diagnostics.ToArray())
        {
            PrimeMeshIds = primeMeshIds, ExplicitLayerIds = explicitLayerIds,
            PrimeObjects = primeObjects, PrimeMaterialNames = primeMaterialNames
        };
    }

    public static void Export(MapDefinition map, string output, string? sidecarPath = null,
        float unitsPerPrimeUnit = DefaultUnits)
        => ExportCore(map, output, sidecarPath, unitsPerPrimeUnit, false);

    internal static void ExportUnderLease(MapDefinition map, string output, string sidecarPath,
        Func<string, byte[]>? nativeAdjunct = null, Action<RadiantSourceCommitPoint>? fault = null,
        bool editorContainers = false, IReadOnlyList<Guid>? triggerIds = null,
        string referenceSource = "", IReadOnlyDictionary<Guid, MapBrush>? legacyBrushes = null)
        => ExportCore(map, output, sidecarPath, DefaultUnits, true, nativeAdjunct, fault, editorContainers, triggerIds, referenceSource, legacyBrushes);

    private static void ExportCore(MapDefinition map, string output, string? sidecarPath,
        float unitsPerPrimeUnit, bool leaseHeld, Func<string, byte[]>? nativeAdjunct = null,
        Action<RadiantSourceCommitPoint>? fault = null, bool editorContainers = false, IReadOnlyList<Guid>? triggerIds = null,
        string referenceSource = "", IReadOnlyDictionary<Guid, MapBrush>? legacyBrushes = null)
    {
        ArgumentNullException.ThrowIfNull(map);
        if (!float.IsFinite(unitsPerPrimeUnit) || unitsPerPrimeUnit <= 0)
            throw new ArgumentOutOfRangeException(nameof(unitsPerPrimeUnit));
        MapValidator.RequireRuntimeName(map.Name);
        if (map.MapId == Guid.Empty)
            throw new InvalidDataException("Cannot export an identity-less map; assign a MapId first.");
        var errors = new List<QuakeMapDiagnostic>();
        if (map.Import != null || map.NativeRoom != null || map.Collision != null
            || map.Brushes.Count > 0 || (!editorContainers && (map.JumpPads.Count > 0
            || map.Q3Triggers?.Count > 0 || map.NavigationLinks.Count > 0)))
            errors.Add(new("FP-RADIANT-EXPORT", 0,
                "Native imported, custom collision and advanced gameplay entities need a lossless exporter.",
                true));

        int index = 0;
        foreach (var geometry in map.Geometry)
        {
            bool nativeMesh = editorContainers && PrimeMeshInterchange.Required(geometry);
            if (!nativeMesh && (geometry is not (MapBox or MapConvexBrush or MapBezierPatch)
                || geometry.Hidden
                || (geometry is not MapBezierPatch && !geometry.Solid)
                || geometry.Damaging
                || geometry.FaceUv.Count != 0
                || geometry.Uv.Rotation != 0
                || geometry.Uv.Offset.Any(v => v != 0)
                || geometry.Uv.Scale.Any(v => v != 1)
                || geometry.Transform.Rotation.Length != 4
                || geometry.Transform.Rotation.Where((v, i) => v != (i == 3 ? 1 : 0)).Any()
                || (geometry is MapConvexBrush
                    && (geometry.Transform.Position.Any(v => v != 0)
                        || geometry.Transform.Scale.Any(v => v != 1)))))
                errors.Add(new("FP-RADIANT-EXPORT-GEOMETRY", index,
                    "Only supported boxes, convex brushes and quadratic patches can round-trip.",
                    true));
            if (geometry.Material < 0 || geometry.Material >= map.Materials.Count)
                errors.Add(new("FP-RADIANT-EXPORT-MATERIAL", index,
                    "Missing material binding.", true));
            if (!nativeMesh && geometry.QuakeFaces is { } projected
                && (projected.Count != (geometry is MapBox ? 6
                    : geometry is MapConvexBrush convex ? convex.Faces.Count : -1)
                    || projected.Any(face => face == null || !face.Valid
                        || face.Material >= map.Materials.Count
                        || Math.Abs(face.UnitsPerPrimeUnit - unitsPerPrimeUnit) > 0.0001f)))
                errors.Add(new("FP-RADIANT-EXPORT-UV", index,
                    "Incomplete Quake face mapping, invalid material, or source unit scale mismatch.",
                    true));
            else if (geometry.Material >= 0 && geometry.Material < map.Materials.Count
                && !nativeMesh && geometry is (MapConvexBrush or MapBezierPatch))
            {
                try { GeometryCompiler.Compile(geometry, map.Materials[geometry.Material].TexScale); }
                catch (MapAuthoringException ex)
                {
                    errors.Add(new("FP-RADIANT-EXPORT-CONVEX", index, ex.Message, true));
                }
            }
            index++;
        }
        if (!editorContainers && map.Spawns.Any(s => s.Team != -1
            || s.Position.Length != 3 || !float.IsFinite(s.Yaw)))
            errors.Add(new("FP-RADIANT-EXPORT-SPAWN", 0,
                "Only neutral fixed-location spawn entities can round-trip.", true));
        if (!editorContainers && map.Items.Any(item => item.Position.Length != 3
            || item.Position.Any(v => !float.IsFinite(v))
            || item.Id == Guid.Empty
            || Q3Gameplay.CanonicalQuakePickup(item.Type) == null))
            errors.Add(new("FP-RADIANT-EXPORT-ITEM", 0,
                "Pickups require valid position, identity and a supported canonical Quake type.", true));
        var ids = map.Geometry.Select(x => x.Id).Concat(map.Spawns.Select(x => x.Id))
            .Concat(map.Items.Select(x => x.Id)).ToArray();
        if (ids.Any(x => x == Guid.Empty) || ids.Distinct().Count() != ids.Length)
            errors.Add(new("FP-RADIANT-EXPORT-ID", 0,
                "Brush, spawn and pickup identities must be nonempty and unique.", true));
        if (errors.Count != 0) throw new QuakeMapInterchangeException(errors.ToArray());

        PrimeMeshInterchange.Material[]? primeMaterials = editorContainers && map.Geometry.Any(PrimeMeshInterchange.Required)
            ? PrimeMeshInterchange.DescribeMaterials(map) : null;
        var sb = new StringBuilder();
        sb.AppendLine("// Project Prime Quake3 .map brush authoring source v2");
        sb.AppendLine("// PRIME_PLANE_WINDING=quake3");
        IReadOnlyDictionary<string, int>? layerIndices = editorContainers
            ? RadiantLayerInterchange.WriteDefinitions(map.Geometry, sb) : null;
        sb.AppendLine("{");
        sb.AppendLine("\"classname\" \"worldspawn\"");
        if (editorContainers) sb.AppendLine("}");
        foreach (MapGeometry geometry in map.Geometry)
        {
            string shader = map.Materials[geometry.Material].Name;
            bool nativeMesh = editorContainers && PrimeMeshInterchange.Required(geometry);
            if (!nativeMesh && !SafeShader(shader))
                throw new InvalidDataException("Shader name is not portable to Quake .map.");
            if (editorContainers)
            {
                sb.AppendLine("{");
                sb.AppendLine("\"classname\" \"func_group\"");
                sb.AppendLine("\"prime_geometry\" \"1\"");
                sb.AppendLine("\"prime_id\" \"" + geometry.Id + "\"");
                if (!nativeMesh && geometry is MapBezierPatch patch)
                {
                    sb.AppendLine("\"prime_patch_solid\" \"" + patch.Solid.ToString().ToLowerInvariant() + "\"");
                    sb.AppendLine("\"prime_patch_tessellation\" \""
                        + patch.Tessellation.ToString(CultureInfo.InvariantCulture) + "\"");
                }
            }
            if (editorContainers) RadiantLayerInterchange.WriteAssignment(layerIndices![geometry.Layer], sb);
            sb.AppendLine("{");
            if (!editorContainers) sb.AppendLine("// PRIME_ID=" + geometry.Id);
            if (nativeMesh)
            {
                PrimeMeshInterchange.Write(geometry, map, unitsPerPrimeUnit, sb, primeMaterials,
                    originalLegacyBrush: legacyBrushes?.GetValueOrDefault(geometry.Id));
                sb.AppendLine("}");
                sb.AppendLine("}");
                continue;
            }
            if (geometry is MapBezierPatch authored)
            {
                if (!editorContainers)
                {
                    sb.AppendLine("// PRIME_PATCH_SOLID=" + authored.Solid.ToString().ToLowerInvariant());
                    sb.AppendLine("// PRIME_PATCH_TESSELLATION="
                        + authored.Tessellation.ToString(CultureInfo.InvariantCulture));
                }
                QuakePatchDef2.Write(authored, shader, unitsPerPrimeUnit, sb);
                sb.AppendLine("}");
                if (editorContainers) sb.AppendLine("}");
                continue;
            }
            static string P(Vector3 p) => "( " + p.X.ToString("G9", CultureInfo.InvariantCulture)
                + " " + p.Y.ToString("G9", CultureInfo.InvariantCulture)
                + " " + p.Z.ToString("G9", CultureInfo.InvariantCulture) + " )";
            void Plane(int faceIndex, Vector3 first, Vector3 second,
                Vector3 third, Vector3 expected)
            {
                if (Vector3.Dot(Vector3.Cross(third - first, second - first), expected) < 0)
                    (second, third) = (third, second);
                MapQuakeFace? face = geometry.QuakeFaces?[faceIndex];
                string faceShader = face == null
                    ? shader : map.Materials[face.Material].Name;
                if (!SafeShader(faceShader))
                    throw new InvalidDataException("Face shader name cannot be represented in Quake .map.");
                static string F(float n) => n.ToString("R", CultureInfo.InvariantCulture);
                string projection = face == null ? "0 0 0 1 1"
                    : F(face.ShiftU) + " " + F(face.ShiftV) + " "
                    + F(face.Rotation) + " " + F(face.ScaleU) + " " + F(face.ScaleV);
                sb.AppendLine(P(first) + " " + P(second) + " " + P(third)
                    + " " + faceShader + " " + projection + " 0 0 0");
            }
            if (geometry is MapBox box)
            {
                float[] p = box.Transform.Position, s = box.Transform.Scale;
                if (p.Length != 3 || s.Length != 3 || s.Any(x => !float.IsFinite(x) || x <= 0)
                    || p.Any(x => !float.IsFinite(x)))
                    throw new InvalidDataException("Invalid authored box transform.");

                float minX = (p[0] - s[0] / 2) * unitsPerPrimeUnit;
                float maxX = (p[0] + s[0] / 2) * unitsPerPrimeUnit;
                float minY = (-p[2] - s[2] / 2) * unitsPerPrimeUnit;
                float maxY = (-p[2] + s[2] / 2) * unitsPerPrimeUnit;
                float minZ = (p[1] - s[1] / 2) * unitsPerPrimeUnit;
                float maxZ = (p[1] + s[1] / 2) * unitsPerPrimeUnit;
                Vector3[] corner =
                {
                    new(minX,minY,minZ), new(maxX,minY,minZ),
                    new(minX,maxY,minZ), new(maxX,maxY,minZ),
                    new(minX,minY,maxZ), new(maxX,minY,maxZ),
                    new(minX,maxY,maxZ), new(maxX,maxY,maxZ)
                };
                // Source plane order is Quake-space -X,+X,-Y,+Y,-Z,+Z;
                // native MapBox faces are -Y,+Y,-Z,+X,+Z,-X.
                Plane(5, corner[0], corner[2], corner[6], -Vector3.UnitX);
                Plane(3, corner[1], corner[5], corner[7], Vector3.UnitX);
                Plane(4, corner[0], corner[4], corner[5], -Vector3.UnitY);
                Plane(2, corner[2], corner[3], corner[7], Vector3.UnitY);
                Plane(0, corner[0], corner[1], corner[3], -Vector3.UnitZ);
                Plane(1, corner[4], corner[6], corner[7], Vector3.UnitZ);
            }
            else if (geometry is MapConvexBrush brush)
            {
                Vector3[] primeVertices = brush.Vertices.Select(v =>
                    new Vector3(v[0], v[1], v[2])).ToArray();
                Vector3 centroid = primeVertices.Aggregate(Vector3.Zero,
                    (sum, point) => sum + point) / primeVertices.Length;
                static Vector3 Q(Vector3 prime, float units)
                    => new(prime.X * units, -prime.Z * units, prime.Y * units);
                Vector3 quakeCenter = Q(centroid, unitsPerPrimeUnit);
                for (int faceIndex = 0; faceIndex < brush.Faces.Count; faceIndex++)
                {
                    int[] face = brush.Faces[faceIndex];
                    Vector3 first = Q(primeVertices[face[0]], unitsPerPrimeUnit);
                    Vector3 second = Q(primeVertices[face[1]], unitsPerPrimeUnit);
                    Vector3 third = Q(primeVertices[face[2]], unitsPerPrimeUnit);
                    Plane(faceIndex, first, second, third, first - quakeCenter);
                }
            }
            sb.AppendLine("}");
            if (editorContainers) sb.AppendLine("}");
        }
        if (!editorContainers) sb.AppendLine("}");
        else
        {
            // Reference primitives have their own protected payloads and no
            // authored layer assignment. Never let the previous geometry's
            // current layer leak into their native scene nodes.
            RadiantLayerInterchange.WriteAssignment(0, sb);
            sb.Append(referenceSource);
        }

        if (editorContainers)
            RadiantObjectInterchange.ExportAppender(map, sb, unitsPerPrimeUnit, triggerIds);
        else
        {
            foreach (MapSpawn spawn in map.Spawns)
            {
                float x = spawn.Position[0] * unitsPerPrimeUnit;
                float y = -spawn.Position[2] * unitsPerPrimeUnit;
                float z = spawn.Position[1] * unitsPerPrimeUnit + 24f;
                float angle = (spawn.Yaw - 90f + 360f) % 360f;
                string Flo(float n) => n.ToString("G9", CultureInfo.InvariantCulture);
                sb.AppendLine("{");
                sb.AppendLine("\"classname\" \"info_player_deathmatch\"");
                sb.AppendLine("\"origin\" \"" + Flo(x) + " " + Flo(y)
                    + " " + Flo(z) + "\"");
                sb.AppendLine("\"angle\" \"" + Flo(angle) + "\"");
                sb.AppendLine("\"prime_id\" \"" + spawn.Id + "\"");
                sb.AppendLine("}");
            }
            // Store Prime-specific pickup timing and base behavior explicitly in
            // source entity keys. Without these, exporting a modified project
            // would silently substitute Quake's unrelated pickup defaults.
            foreach (MapItem item in map.Items)
            {
                string type = Q3Gameplay.CanonicalQuakePickup(item.Type)!;
                float x = item.Position[0] * unitsPerPrimeUnit;
                float y = -item.Position[2] * unitsPerPrimeUnit;
                float z = item.Position[1] * unitsPerPrimeUnit;
                string Flo(float n) => n.ToString("G9", CultureInfo.InvariantCulture);
                sb.AppendLine("{");
                sb.AppendLine("\"classname\" \"" + type + "\"");
                sb.AppendLine("\"origin\" \"" + Flo(x) + " " + Flo(y) + " " + Flo(z) + "\"");
                sb.AppendLine("\"prime_id\" \"" + item.Id + "\"");
                sb.AppendLine("\"prime_spawn_interval\" \""
                    + item.SpawnInterval.ToString(CultureInfo.InvariantCulture) + "\"");
                sb.AppendLine("\"prime_has_base\" \"" + item.HasBase.ToString() + "\"");
                sb.AppendLine("}");
            }

        }

        string full = Path.GetFullPath(output);
        if (!Path.GetExtension(full).Equals(".map", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Quake source exports require a .map destination.");
        string sidecar = Path.GetFullPath(sidecarPath ?? Path.Combine(
            Path.GetDirectoryName(full)!, "project-prime.json"));
        if (string.Equals(full, sidecar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Quake source and identity sidecar must be distinct files.");
        using var sourceLease = leaseHeld ? null : RadiantSourceTransaction.Acquire(full);
        RadiantSourceTransaction.RecoverUnderLease(full, sidecar);
        string? sourceBefore = File.Exists(full) ? MapBuildFingerprint.HashFile(full) : null;
        string? sidecarBefore = File.Exists(sidecar) ? MapBuildFingerprint.HashFile(sidecar) : null;
        QuakeMapSidecar? previous = null;
        if (File.Exists(full) && File.GetAttributes(full).HasFlag(FileAttributes.ReparsePoint))
            throw new InvalidDataException("Refusing to overwrite a symbolic-link .map source.");
        if (File.Exists(sidecar))
        {
            if (File.GetAttributes(sidecar).HasFlag(FileAttributes.ReparsePoint))
                throw new InvalidDataException("Refusing to overwrite a symbolic-link identity sidecar.");
            try
            {
                if (new FileInfo(sidecar).Length > 65536)
                    throw new InvalidDataException("Existing Quake sidecar is oversized.");
                previous = ReadSidecarStrict(File.ReadAllBytes(sidecar));
            }
            catch (JsonException ex)
            {
                throw new InvalidDataException("Refusing to overwrite an unrelated identity sidecar.", ex);
            }
            if (previous?.MapId != map.MapId || previous.SchemaVersion != 1)
                throw new InvalidDataException(
                    "Refusing to overwrite a sidecar belonging to a different map identity.");
        }
        // An existing .map is an independently editable NetRadiant master.
        // An export may replace it only if its sidecar binds the same MapId
        // and still matches the bytes that were previously exported. Otherwise
        // the author's changes must be imported/reconciled, not overwritten.
        if (File.Exists(full)
            && (previous == null
                || !previous.SourceSha256.Equals(MapBuildFingerprint.HashFile(full),
                    StringComparison.OrdinalIgnoreCase)))
            throw new IOException("Quake .map changed since its identity sidecar was written "
                + "or has no trusted sidecar. Import the updated source or export to a new path.");
        // Refuse identity/source conflicts before modifying either source or sidecar.
        byte[] sourceBytes = Encoding.UTF8.GetBytes(sb.ToString());
        byte[] sidecarBytes = JsonSerializer.SerializeToUtf8Bytes(
            new QuakeMapSidecar(1, map.MapId, map.Name, unitsPerPrimeUnit,
                Convert.ToHexString(SHA256.HashData(sourceBytes)).ToLowerInvariant()),
            MapPackageReader.JsonOptions);
        byte[]? adjunctBytes = nativeAdjunct?.Invoke(Convert.ToHexString(SHA256.HashData(sourceBytes)).ToLowerInvariant());
        string? adjunctPath = adjunctBytes == null ? null : full + ".prime-source.json";
        string? adjunctBefore = adjunctPath != null && File.Exists(adjunctPath) ? MapBuildFingerprint.HashFile(adjunctPath) : null;
        RadiantSourceTransaction.CommitUnderLease(full, sidecar, sourceBytes, sidecarBytes, sourceBefore, sidecarBefore,
            fault, adjunctPath, adjunctBytes, adjunctBefore);
    }

    private static bool SafeShader(string? shader)
    {
        if (string.IsNullOrEmpty(shader) || shader.Length > 128
            || shader.StartsWith("/", StringComparison.Ordinal)
            || shader.EndsWith("/", StringComparison.Ordinal)
            || shader.Contains("..", StringComparison.Ordinal)
            || shader.Contains("//", StringComparison.Ordinal))
            return false;
        foreach (char ch in shader)
            if (!(ch is >= 'a' and <= 'z' or >= 'A' and <= 'Z'
                or >= '0' and <= '9' or '_' or '-' or '.' or '/'))
                return false;
        return true;
    }

    public static QuakeMapSidecar ReadIdentity(string path)
    {
        if (!File.Exists(path) || new FileInfo(path).Length > 65536)
            throw new InvalidDataException("Missing or oversized Quake identity sidecar.");
        return ReadIdentityBytes(File.ReadAllBytes(path));
    }

    internal static QuakeMapSidecar ReadIdentityBytes(byte[] bytes)
    {
        var sidecar = ReadSidecarStrict(bytes);
        if (sidecar?.SchemaVersion != 1 || sidecar.MapId == Guid.Empty
            || !float.IsFinite(sidecar.UnitsPerPrimeUnit)
            || sidecar.UnitsPerPrimeUnit is < 1 or > 1024
            || sidecar.SourceSha256 is not { Length: 64 }
            || sidecar.SourceSha256.Any(c => !Uri.IsHexDigit(c)))
            throw new InvalidDataException("Invalid Quake identity sidecar.");
        return sidecar;
    }

    private static QuakeMapSidecar? ReadSidecarStrict(byte[] bytes)
    {
        // MapId is not enough to prevent destructive overwrite. A richer
        // sidecar may contain creator metadata not yet expressible by this
        // restricted interchange, so unknown and duplicate JSON keys must
        // fail instead of being dropped when a newer source is exported.
        using JsonDocument document = JsonDocument.Parse(bytes);
        if (document.RootElement.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("Expected an object for Quake map identity sidecar.");
        var supported = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "schemaVersion", "mapId", "roomName", "unitsPerPrimeUnit", "sourceSha256"
        };
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (JsonProperty property in document.RootElement.EnumerateObject())
        {
            if (!supported.Contains(property.Name))
                throw new InvalidDataException(
                    "Unsupported sidecar property " + property.Name
                    + " must be preserved by a newer interchange version.");
            if (!seen.Add(property.Name))
                throw new InvalidDataException(
                    "Duplicate Quake sidecar property " + property.Name + ".");
        }
        return JsonSerializer.Deserialize<QuakeMapSidecar>(
            bytes, MapPackageReader.JsonOptions);
    }

    private static Guid GuidFrom(string key)
        => new Guid(SHA256.HashData(Encoding.UTF8.GetBytes(key)).AsSpan(0, 16));
}

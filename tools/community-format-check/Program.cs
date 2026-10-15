using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Text.Json;
using System.Text.Json.Serialization;
using MphRead.Mods.MapGen;

string root = Path.Combine(Path.GetTempPath(),
    "prime-map-interchange-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
int checks = 0;
void Check(bool valid, string label)
{
    if (!valid) throw new InvalidDataException(label);
    checks++;
    Console.WriteLine("PASS " + label);
}
try
{
    Guid mapId = Guid.Parse("578e96bb-c99a-491c-a47a-923d0d2849bc");
    Guid firstId = Guid.Parse("e97a2995-4597-4edf-8cd2-00839424d7a3");
    Guid spawnId = Guid.Parse("744d9a23-5825-4648-90ae-506ab6a3e6fe");
    var map = new MapDefinition
    {
        FormatVersion = 2, MapId = mapId,
        Name = "AXIAL_DUEL", InGameName = "Axial Duel", Version = "1"
    };
    map.Materials.Add(new MapMaterial
    {
        Id = Guid.Parse("c983cb13-f253-4baa-aa33-3dd93a831b78"),
        Name = "textures/common/caulk", SourceMaterial = 2
    });
    map.Geometry.Add(new MapBox
    {
        Id = firstId, Material = 0,
        Transform = new MapTransform
        {
            Position = new[] { 0f, -2f, 0f },
            Scale = new[] { 12f, 2f, 16f }
        }
    });
    map.Geometry.Add(new MapBox
    {
        Id = Guid.Parse("843e73c7-e89e-4014-8ae2-313128478119"),
        Material = 0,
        Transform = new MapTransform
        {
            Position = new[] { 4f, 1f, -3f },
            Scale = new[] { 2f, 3f, 5f }
        }
    });
    map.Spawns.Add(new MapSpawn
    {
        Id = spawnId, Position = new[] { 1f, 2f, -2f }, Yaw = 45f
    });
    string path = Path.Combine(root, "editable.map");
    string sidecar = Path.Combine(root, "project-prime.json");
    QuakeMapInterchange.Export(map, path, sidecar, 16f);
    Check(File.Exists(path) && File.Exists(sidecar)
        && File.ReadAllText(path).Contains("PRIME_ID=" + firstId),
        "source exporter retains brush GUID and external identity sidecar");

    string legacy = Path.Combine(root, "legacy-prime-v1.map");
    string legacyText = File.ReadAllText(path)
        .Replace("// Project Prime Quake3 .map brush authoring source v2", "// Project Prime original Quake3 .map brush authoring source", StringComparison.Ordinal)
        .Replace("// PRIME_PLANE_WINDING=quake3" + Environment.NewLine, "", StringComparison.Ordinal);
    // Recreate the historical export format explicitly: the old Prime header
    // selected Cross(d1,d2), opposite to the actual Quake 3 plane convention.
    legacyText = Regex.Replace(legacyText,
        @"(?m)^(?<a>\( [^\r\n)]+ \)) (?<b>\( [^\r\n)]+ \)) (?<c>\( [^\r\n)]+ \))",
        "${a} ${c} ${b}");
    File.WriteAllText(legacy, legacyText);
    var legacyMap = QuakeMapInterchange.Import(legacy, sidecar);
    Check(legacyMap.Definition.Geometry.Count == map.Geometry.Count
        && ((MapBox)legacyMap.Definition.Geometry[0]).Transform.Scale.SequenceEqual(((MapBox)map.Geometry[0]).Transform.Scale),
        "explicit historical Prime v1 header preserves legacy source geometry");
    string unmarkedLegacy = Path.Combine(root, "unmarked-legacy.map");
    File.WriteAllText(unmarkedLegacy, legacyText.Replace(
        "// Project Prime original Quake3 .map brush authoring source", "// No format marker", StringComparison.Ordinal));
    bool unmarkedRefused = false;
    try { QuakeMapInterchange.Import(unmarkedLegacy); }
    catch (QuakeMapInterchangeException) { unmarkedRefused = true; }
    Check(unmarkedRefused, "unmarked inverted Quake planes are refused without heuristic winding repair");

    var identityless1 = QuakeMapInterchange.Import(path);
    var identityless2 = QuakeMapInterchange.Import(path);
    Check(identityless1.Definition.MapId != Guid.Empty
        && identityless2.Definition.MapId != Guid.Empty
        && identityless1.Definition.MapId != identityless2.Definition.MapId
        && identityless1.Diagnostics.Any(d => d.Code == "FP-RADIANT-NEW-ID"),
        "new source imports without pinned sidecars cannot collide by filename");

    var imported = QuakeMapInterchange.Import(path, sidecar);
    Check(imported.Definition.MapId == mapId
        && imported.Definition.Geometry.Count == 2
        && imported.Definition.Spawns.Count == 1
        && imported.Definition.Spawns[0].Id == spawnId
        && imported.Definition.Geometry[0].Id == firstId,
        "direct import preserves map/brush/spawn stable identities");

    for (int i = 0; i < map.Geometry.Count; i++)
    {
        MapBox expected = (MapBox)map.Geometry[i], actual = (MapBox)imported.Definition.Geometry[i];
        Check(expected.Transform.Position.Zip(actual.Transform.Position,
                (x, y) => Math.Abs(x-y)).All(x => x <= 0.0001f)
            && expected.Transform.Scale.Zip(actual.Transform.Scale,
                (x, y) => Math.Abs(x-y)).All(x => x <= 0.0001f),
            "round-trip axial bounds preserve native geometry/collision coordinates");
    }
    Check(Math.Abs(map.Spawns[0].Yaw - imported.Definition.Spawns[0].Yaw) < 0.001f
        && map.Spawns[0].Position.Zip(imported.Definition.Spawns[0].Position,
            (x, y) => Math.Abs(x-y)).All(x => x < 0.001f),
        "Q3 (x,z,-y) transform and spawn height/angle survive round-trip");

    string again = Path.Combine(root, "again.map");
    QuakeMapInterchange.Export(imported.Definition, again,
        Path.Combine(root, "again-sidecar.json"), 16f);
    var importedAgain = QuakeMapInterchange.Import(
        again, Path.Combine(root, "again-sidecar.json"));
    Check(importedAgain.Definition.MapId == mapId
        && importedAgain.Definition.Geometry.Select(x => x.Id)
            .SequenceEqual(imported.Definition.Geometry.Select(x => x.Id)),
        "second export/import is stable for supported source geometry");

    // q3map classic floor/ceiling axes: +Z normal uses U=+QuakeX,
    // V=-QuakeY. Prime (1,0,2) maps to Quake (16,-32,0).
    var projection = new MapQuakeFace
    {
        Material = 0, ShiftU = 5, ShiftV = -7,
        Rotation = 0, ScaleU = 2, ScaleV = -4, UnitsPerPrimeUnit = 16
    };
    var exactUv = QuakeClassicTexture.Calculate(
        new System.Numerics.Vector3(1, 0, 2),
        System.Numerics.Vector3.UnitY, projection);
    Check(Math.Abs(exactUv.X - 13f) < 0.001f
        && Math.Abs(exactUv.Y + 15f) < 0.001f,
        "Quake floor texture shift and negative scale follow q3map projection axes");
    var turnedUv = QuakeClassicTexture.Calculate(
        new System.Numerics.Vector3(1, 0, 2),
        System.Numerics.Vector3.UnitY,
        new MapQuakeFace { Material = 0, Rotation = 90, UnitsPerPrimeUnit = 16 });
    Check(Math.Abs(turnedUv.X + 32f) < 0.001f
        && Math.Abs(turnedUv.Y - 16f) < 0.001f,
        "Quake 90-degree face rotation preserves original q3map UV orientation");

    string uv = Path.Combine(root, "uv-supported.map");
    File.WriteAllText(uv, File.ReadAllText(path).Replace(
        " 0 0 0 1 1 0 0 0", " 4 0 0 1 1 0 0 0", StringComparison.Ordinal));
    var projectedMap = QuakeMapInterchange.Import(uv);
    Check(projectedMap.Definition.Geometry.All(geo => geo is MapBox
        && geo.QuakeFaces is { Count: 6 }
        && geo.QuakeFaces.All(face => face.ShiftU == 4)),
        "nondefault classic Quake texture offsets retain editable box geometry");
    string uvRoundtrip = Path.Combine(root, "uv-supported-roundtrip.map");
    QuakeMapInterchange.Export(projectedMap.Definition, uvRoundtrip,
        Path.Combine(root, "uv-supported-sidecar.json"));
    var projectedAgain = QuakeMapInterchange.Import(uvRoundtrip);
    Check(projectedAgain.Definition.Geometry.All(g =>
        g.QuakeFaces is { Count: 6 } && g.QuakeFaces.All(face => face.ShiftU == 4)),
        "classic UV shift survives a second source round-trip");

    string mixed = Path.Combine(root, "mixed-face.map");
    string original = File.ReadAllText(path);
    int shaderIndex = original.IndexOf("textures/common/caulk 0 0 0 1 1 0 0 0",
        StringComparison.Ordinal);
    if (shaderIndex < 0) throw new InvalidDataException("Missing source shader fixture.");
    File.WriteAllText(mixed, original[..shaderIndex]
        + "textures/custom/steel 32 -16 90 0.5 2 0 0 0"
        + original[(shaderIndex+"textures/common/caulk 0 0 0 1 1 0 0 0".Length)..]);
    var mixedMap = QuakeMapInterchange.Import(mixed);
    MapBox authoredBox = (MapBox)mixedMap.Definition.Geometry.First();
    Check(mixedMap.Definition.Materials.Count == 2
        && authoredBox.QuakeFaces is { Count: 6 }
        && authoredBox.QuakeFaces.Select(face => face.Material).Distinct().Count() == 2
        && authoredBox.QuakeFaces.Any(face => face.ShiftU == 32
            && face.ShiftV == -16 && face.Rotation == 90
            && face.ScaleU == 0.5f && face.ScaleV == 2f),
        "independent box-face shader, UV translation, rotation and scale survive import");
    Check(GeometryCompiler.Compile(authoredBox, 16f)
        .Select(face => face.Material).Distinct().Count() == 2,
        "native solid geometry compiler respects Quake per-face materials");
    var serialized = MapProjectSerializer.Clone(mixedMap.Definition);
    Check(serialized.Geometry[0].QuakeFaces?.Count == 6,
        "editable source projections survive native project JSON clone");
    string mixedRoundtrip = Path.Combine(root, "mixed-face-roundtrip.map");
    QuakeMapInterchange.Export(mixedMap.Definition, mixedRoundtrip,
        Path.Combine(root, "mixed-sidecar.json"));
    var mixedAgain = QuakeMapInterchange.Import(mixedRoundtrip);
    Check(mixedAgain.Definition.Geometry[0].QuakeFaces?.Any(face =>
        face.Rotation == 90 && face.ShiftU == 32 && face.ScaleV == 2) == true,
        "mixed shader and rotated Quake texdefs survive source export and reimport");
    var portableBoxProject = MapProjectSerializer.Clone(mixedMap.Definition);
    Check(MapPackageGeometryCompatibility.LowerInPlace(portableBoxProject)
        && portableBoxProject.Geometry[0] is MapMesh portableBox
        && portableBox.Vertices.Count == 8
        && portableBox.Faces.Count == 6
        && portableBox.FaceMaterials.Distinct().Count() == 2
        && portableBox.FaceTexcoords.Count == 6,
        "portable mixed-face box preserves shared topology, per-face shaders and UVs");

    bool refused = false;
    string badUv = Path.Combine(root, "uv-unsupported-flags.map");
    File.WriteAllText(badUv, original.Replace(
        " 0 0 0 1 1 0 0 0", " 0 0 0 1 1 1 0 0", StringComparison.Ordinal));
    try { QuakeMapInterchange.Import(badUv); }
    catch (QuakeMapInterchangeException ex)
    { refused = ex.Diagnostics.Any(d => d.Code == "FP-RADIANT-UV"); }
    Check(refused, "unknown Quake surface flags remain explicitly unsupported");
    string unsafeShader = Path.Combine(root, "unsafe-shader.map");
    File.WriteAllText(unsafeShader, original.Replace(
        "textures/common/caulk", "textures/../foreign/asset",
        StringComparison.Ordinal));
    refused = false;
    try { QuakeMapInterchange.Import(unsafeShader); }
    catch (QuakeMapInterchangeException ex)
    { refused = ex.Diagnostics.Any(d => d.Code == "FP-RADIANT-UV"); }
    Check(refused, "untrusted shader traversal path is rejected before material registration");

    string patch = Path.Combine(root, "patch-unsupported.map");
    File.WriteAllText(patch, "{\n\"classname\" \"worldspawn\"\n{\npatchDef2\n{\n}\n}\n}\n");
    refused = false;
    try { QuakeMapInterchange.Import(patch); }
    catch (QuakeMapInterchangeException ex)
    { refused = ex.Diagnostics.Any(d => d.Code == "FP-RADIANT-PATCH"); }
    Check(refused, "unsupported Bezier patch emits a concrete error instead of losing geometry");

    string unknown = Path.Combine(root, "entity-unsupported.map");
    File.WriteAllText(unknown, File.ReadAllText(path)
        + "\n{\n\"classname\" \"func_door\"\n}\n");
    refused = false;
    try { QuakeMapInterchange.Import(unknown); }
    catch (QuakeMapInterchangeException ex)
    { refused = ex.Diagnostics.Any(d => d.Code == "FP-RADIANT-ENTITY"); }
    Check(refused, "unsupported gameplay entity cannot disappear without diagnostics");

    // Untrusted sidecars cannot override the validated import scale.
    var goodSidecar = JsonSerializer.Deserialize<QuakeMapSidecar>(
        File.ReadAllText(sidecar), MapPackageReader.JsonOptions)!;
    string badScaleSidecar = Path.Combine(root, "invalid-scale.json");
    File.WriteAllText(badScaleSidecar, JsonSerializer.Serialize(
        goodSidecar with { UnitsPerPrimeUnit = float.NaN },
        new JsonSerializerOptions
        { NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals }));
    refused = false;
    try { QuakeMapInterchange.Import(path, badScaleSidecar); }
    catch (Exception ex) when (ex is InvalidDataException or JsonException)
    { refused = true; }
    Check(refused, "nonfinite sidecar scale cannot poison imported world coordinates");

    File.WriteAllText(badScaleSidecar, JsonSerializer.Serialize(
        goodSidecar with { UnitsPerPrimeUnit = 0f }));
    refused = false;
    try { QuakeMapInterchange.Import(path, badScaleSidecar); }
    catch (InvalidDataException) { refused = true; }
    Check(refused, "zero sidecar scale is rejected after sidecar resolution");

    string unknownWorldspawn = Path.Combine(root, "worldspawn-metadata.map");
    File.WriteAllText(unknownWorldspawn, File.ReadAllText(path).Replace(
        "\"classname\" \"worldspawn\"",
        "\"classname\" \"worldspawn\"\n\"message\" \"DO NOT DROP\"", StringComparison.Ordinal));
    refused = false;
    try { QuakeMapInterchange.Import(unknownWorldspawn); }
    catch (QuakeMapInterchangeException ex)
    { refused = ex.Diagnostics.Any(d => d.Code == "FP-RADIANT-WORLDSPAWN"); }
    Check(refused, "unrepresented worldspawn properties are not silently discarded");

    string badGuid = Path.Combine(root, "bad-spawn-id.map");
    File.WriteAllText(badGuid, File.ReadAllText(path).Replace(
        "\"prime_id\" \"" + spawnId + "\"",
        "\"prime_id\" \"not-a-guid\"", StringComparison.Ordinal));
    refused = false;
    try { QuakeMapInterchange.Import(badGuid); }
    catch (QuakeMapInterchangeException ex)
    { refused = ex.Diagnostics.Any(d => d.Code == "FP-RADIANT-ID"); }
    Check(refused, "invalid authored spawn GUID is an error, not a silent replacement");

    string duplicateGuid = Path.Combine(root, "duplicate-brush-id.map");
    File.WriteAllText(duplicateGuid, File.ReadAllText(path).Replace(
        "PRIME_ID=843e73c7-e89e-4014-8ae2-313128478119",
        "PRIME_ID=" + firstId, StringComparison.Ordinal));
    refused = false;
    try { QuakeMapInterchange.Import(duplicateGuid); }
    catch (QuakeMapInterchangeException ex)
    { refused = ex.Diagnostics.Any(d => d.Code == "FP-RADIANT-ID"); }
    Check(refused, "duplicate brush GUID cannot silently overwrite authoring identity");

    string conflictSidecar = Path.Combine(root, "other-map-sidecar.json");
    File.WriteAllText(conflictSidecar, JsonSerializer.Serialize(
        goodSidecar with { MapId = Guid.NewGuid() }));
    string conflictOutput = Path.Combine(root, "source-must-remain-untouched.map");
    refused = false;
    try { QuakeMapInterchange.Export(map, conflictOutput, conflictSidecar); }
    catch (InvalidDataException) { refused = true; }
    Check(refused && !File.Exists(conflictOutput),
        "export rejects unrelated sidecar identity without modifying source");

    // PR-13 convex stage: NetRadiant-authored ramps/angled cover must
    // remain truly editable closed Prime geometry, not baked BSP.
    Guid rampId = Guid.Parse("e3612416-e19a-4d8a-8870-25adcf0576d8");
    var ramp = new MapConvexBrush
    {
        Id = rampId,
        Material = 0,
        Label = "Sloped competitive ramp",
        Vertices = new()
        {
            new[] { 0f, 0f, 0f }, new[] { 8f, 0f, 0f },
            new[] { 0f, 0f, -8f }, new[] { 8f, 0f, -8f },
            new[] { 0f, 4f, -8f }, new[] { 8f, 4f, -8f }
        },
        Faces = new()
        {
            new[] { 0, 2, 3, 1 }, // solid base
            new[] { 0, 4, 2 },    // left triangle
            new[] { 1, 3, 5 },    // right triangle
            new[] { 2, 4, 5, 3 }, // back face
            new[] { 0, 1, 5, 4 }  // sloped surface
        }
    };
    Check(GeometryCompiler.Compile(ramp, 16f).Count == 5,
        "authored wedge has 5 native collision/render planes before Quake conversion");
    var rampMap = new MapDefinition
    {
        FormatVersion = 2, MapId = Guid.NewGuid(), Name = "CONVEX_DUEL"
    };
    rampMap.Materials.Add(new MapMaterial
    {
        Id = Guid.NewGuid(), Name = "textures/common/caulk", SourceMaterial = 2
    });
    rampMap.Materials.Add(new MapMaterial
    {
        Id = Guid.NewGuid(), Name = "textures/arena/ramp_trim", SourceMaterial = 2
    });
    ramp.QuakeFaces = Enumerable.Range(0, 5).Select(i => new MapQuakeFace
    {
        Material = i == 4 ? 1 : 0,
        ShiftU = i == 4 ? 12f : 0f,
        ShiftV = i == 4 ? -8f : 0f,
        Rotation = i == 4 ? 15f : 0f,
        ScaleU = i == 4 ? 0.5f : 1f,
        ScaleV = i == 4 ? 2f : 1f,
        UnitsPerPrimeUnit = 16
    }).ToList();
    rampMap.Geometry.Add(ramp);
    Guid itemId = Guid.Parse("d3be52b8-4eca-49d8-95af-86b9e6ab811e");
    rampMap.Items.Add(new MapItem
    {
        Id = itemId, Position = new[] { 9f, 4f, 6f },
        Type = "HealthMedium", SpawnInterval = 720, HasBase = false
    });
    Check(Q3Gameplay.MapPickupType("weapon_railgun").ToString() == "Imperialist"
        && Q3Gameplay.CanonicalQuakePickup("HealthMedium") == "item_health",
        "shared native Q3 gameplay pickup mapping remains available to both import paths");
    rampMap.Spawns.Add(new MapSpawn
    {
        Id = Guid.NewGuid(), Position = new[] { 30f, 5f, 30f }, Yaw = 0
    });
    string rampSource = Path.Combine(root, "ramp.map");
    string rampSidecar = Path.Combine(root, "ramp-sidecar.json");
    QuakeMapInterchange.Export(rampMap, rampSource, rampSidecar);
    var rampParsed = QuakeMapInterchange.Import(rampSource, rampSidecar);
    Check(rampParsed.Definition.MapId == rampMap.MapId
        && rampParsed.Definition.Geometry.Single() is MapConvexBrush,
        "nonaxial sloped brush imports as native editable convex geometry");
    Check(rampParsed.Definition.Items.Single().Id == itemId
        && rampParsed.Definition.Items.Single().Type == "HealthMedium"
        && rampParsed.Definition.Items.Single().SpawnInterval == 720
        && !rampParsed.Definition.Items.Single().HasBase,
        "Quake pickup identity, native type, respawn interval and base policy survive");
    MapConvexBrush convertedRamp = (MapConvexBrush)rampParsed.Definition.Geometry.Single();
    Check(convertedRamp.QuakeFaces is { Count: 5 }
        && convertedRamp.QuakeFaces.Select(f => f.Material).Distinct().Count() == 2
        && convertedRamp.QuakeFaces.Any(f => f.Rotation == 15
            && f.ShiftU == 12 && f.ShiftV == -8 && f.ScaleU == 0.5f),
        "angled ramp retains exact independent shader and UV projection per face");
    Check(GeometryCompiler.Compile(convertedRamp, 16f)
        .Select(f => f.Material).Distinct().Count() == 2,
        "native convex renderer uses independent face materials");
    string portable = Path.Combine(root, "ramp-portable.json");
    rampParsed.Definition.Save(portable);
    MapDefinition reloaded = MapDefinition.Load(portable);
    var reloadedFaces = reloaded.Geometry[0].QuakeFaces;
    Check(reloadedFaces is { Count: 5 }
        && reloadedFaces.Select(f => reloaded.Materials[f.Material].Name)
            .Distinct().Count() == 2,
        "deterministic JSON save remaps Quake face materials without changing shader identity");
    Check(convertedRamp.Id == rampId
        && convertedRamp.Vertices.Count == 6
        && convertedRamp.Faces.Count == 5
        && GeometryCompiler.Compile(convertedRamp, 16f).Count == 5,
        "wedge retains native face count, stable ID and solid collision topology");

    string rampAgain = Path.Combine(root, "ramp-again.map");
    string rampAgainSidecar = Path.Combine(root, "ramp-again-sidecar.json");
    QuakeMapInterchange.Export(rampParsed.Definition, rampAgain, rampAgainSidecar);
    var rampAgainParsed = QuakeMapInterchange.Import(rampAgain, rampAgainSidecar);
    MapConvexBrush twiceConverted = (MapConvexBrush)rampAgainParsed.Definition.Geometry.Single();
    Check(twiceConverted.QuakeFaces is { Count: 5 }
        && twiceConverted.QuakeFaces.Any(f => f.ShiftU == 12
            && f.Rotation == 15 && f.ScaleU == 0.5f),
        "angled brush classic per-face texture projection survives repeated export");
    Check(rampAgainParsed.Definition.Items.Single().Id == itemId
        && rampAgainParsed.Definition.Items.Single().SpawnInterval == 720,
        "repeated source export does not reset native pickup respawn semantics");
    Check(twiceConverted.Id == convertedRamp.Id && twiceConverted.Faces.Count == 5
        && twiceConverted.Vertices.All(p => convertedRamp.Vertices.Any(q =>
            p.Zip(q, (a, b) => Math.Abs(a - b)).All(delta => delta <= 0.001f))),
        "second convex source round-trip retains every hull corner and 5 planes");

    string invertedSource = Path.Combine(root, "inverted-ramp.map");
    var sourceLines = File.ReadAllLines(rampSource);
    int planeLine = Array.FindIndex(sourceLines, l => l.TrimStart().StartsWith("(", StringComparison.Ordinal));
    if (planeLine < 0) throw new InvalidDataException("Exported ramp has no face records.");
    var sourcePoints = Regex.Matches(sourceLines[planeLine], @"\([^)]*\)")
        .Select(m => m.Value).ToArray();
    sourceLines[planeLine] = sourcePoints[0] + " " + sourcePoints[2] + " "
        + sourcePoints[1] + " textures/common/caulk 0 0 0 1 1 0 0 0";
    File.WriteAllLines(invertedSource, sourceLines);
    refused = false;
    try { QuakeMapInterchange.Import(invertedSource); }
    catch (QuakeMapInterchangeException ex)
    { refused = ex.Diagnostics.Any(d => d.Code == "FP-RADIANT-CONVEX"); }
    Check(refused, "inward-facing brush plane is refused rather than inverted silently");

    string itemMetadata = Path.Combine(root, "unsupported-pickup-property.map");
    File.WriteAllText(itemMetadata, File.ReadAllText(rampSource).Replace(
        "\"prime_has_base\" \"False\"",
        "\"prime_has_base\" \"False\"\n\"targetname\" \"must-preserve\"",
        StringComparison.Ordinal));
    refused = false;
    try { QuakeMapInterchange.Import(itemMetadata); }
    catch (QuakeMapInterchangeException ex)
    { refused = ex.Diagnostics.Any(d => d.Code == "FP-RADIANT-ITEM"); }
    Check(refused, "foreign pickup script metadata is rejected rather than dropped");

    string duplicateItem = Path.Combine(root, "duplicate-pickup-id.map");
    File.WriteAllText(duplicateItem, File.ReadAllText(rampSource).Replace(
        "\"prime_id\" \"" + itemId + "\"",
        "\"prime_id\" \"" + rampId + "\"", StringComparison.Ordinal));
    refused = false;
    try { QuakeMapInterchange.Import(duplicateItem); }
    catch (QuakeMapInterchangeException ex)
    { refused = ex.Diagnostics.Any(d => d.Code == "FP-RADIANT-ID"); }
    Check(refused, "pickup GUID colliding with brush identity is refused");

    string badInterval = Path.Combine(root, "invalid-pickup-timing.map");
    File.WriteAllText(badInterval, File.ReadAllText(rampSource).Replace(
        "\"prime_spawn_interval\" \"720\"",
        "\"prime_spawn_interval\" \"-42\"", StringComparison.Ordinal));
    refused = false;
    try { QuakeMapInterchange.Import(badInterval); }
    catch (QuakeMapInterchangeException ex)
    { refused = ex.Diagnostics.Any(d => d.Code == "FP-RADIANT-ITEM"); }
    Check(refused, "pickup timing cannot overflow/underflow native ushort metadata");

    var expansive = new MapDefinition
    {
        FormatVersion = 2, MapId = Guid.NewGuid(), Name = "WIDE_ARENA"
    };
    expansive.Materials.Add(new MapMaterial
    {
        Id = Guid.NewGuid(), Name = "textures/common/caulk", SourceMaterial = 2
    });
    expansive.Geometry.Add(new MapBox
    {
        Id = Guid.NewGuid(), Material = 0,
        Transform = new MapTransform
        {
            Position = new[] { 300f, 2f, 0f },
            Scale = new[] { 4f, 4f, 4f }
        }
    });
    expansive.Spawns.Add(new MapSpawn
    {
        Id = Guid.NewGuid(), Position = new[] { 300f, 5f, 6f }
    });
    string wideSource = Path.Combine(root, "wide.map");
    string wideSidecar = Path.Combine(root, "wide-sidecar.json");
    QuakeMapInterchange.Export(expansive, wideSource, wideSidecar);
    var wideImported = QuakeMapInterchange.Import(wideSource, wideSidecar);
    Check(wideImported.Definition.ScaleFactor >= 6
        && wideImported.Definition.Geometry.Single() is MapBox wideBox
        && Math.Abs(wideBox.Transform.Position[0] - 300f) <= 0.001f,
        "fixed-point scale increases for a wide Quake arena without moving geometry");

    string richerSidecar = Path.Combine(root, "unrepresented-metadata-sidecar.json");
    string canonicalSidecar = File.ReadAllText(sidecar);
    int closeBrace = canonicalSidecar.LastIndexOf('}');
    if (closeBrace < 0) throw new InvalidDataException("Sidecar fixture is malformed.");
    File.WriteAllText(richerSidecar, canonicalSidecar[..closeBrace].TrimEnd()
        + ",\n\"creatorAttribution\":\"MUST NOT BE DISCARDED\"\n}");
    refused = false;
    try { QuakeMapInterchange.Import(path, richerSidecar); }
    catch (InvalidDataException) { refused = true; }
    Check(refused, "unknown creator sidecar fields cannot silently disappear on import");

    string unsafeRewrite = Path.Combine(root, "unsafe-sidecar-overwrite.map");
    refused = false;
    try { QuakeMapInterchange.Export(map, unsafeRewrite, richerSidecar); }
    catch (InvalidDataException) { refused = true; }
    Check(refused && !File.Exists(unsafeRewrite),
        "refuse to replace a richer sidecar without mutating the Quake source");

    // This is an independently authored, valid NetRadiant patchDef2 grid,
    // not text round-tripped from our own exporter. Columns come first.
    string patchSource = Path.Combine(root, "netradiant-quadratic.map");
    File.WriteAllText(patchSource, """
    {
    "classname" "worldspawn"
    {
    // PRIME_ID=18365f80-bc63-4e93-a798-3b52ab89e4e5
    // PRIME_PATCH_SOLID=false
    // PRIME_PATCH_TESSELLATION=2
    patchDef2
    {
    textures/arena/curve
    ( 3 3 0 0 0 )
    (
    ( ( 0 0 0 0 0 ) ( 0 16 0 0 0.5 ) ( 0 32 0 0 1 ) )
    ( ( 16 0 0 0.5 0 ) ( 16 16 16 0.5 0.5 ) ( 16 32 0 0.5 1 ) )
    ( ( 32 0 0 1 0 ) ( 32 16 0 1 0.5 ) ( 32 32 0 1 1 ) )
    )
    }
    }
    }
    {
    "classname" "info_player_deathmatch"
    "origin" "128 128 64"
    "angle" "90"
    }
    """);
    var curved = QuakeMapInterchange.Import(patchSource, roomName: "CURVE_TEST");
    Check(curved.Definition.Geometry.Count == 1
        && curved.Definition.Geometry[0] is MapBezierPatch,
        "native .map import retains quadratic control grid rather than a baked BSP");
    var editablePatch = (MapBezierPatch)curved.Definition.Geometry.Single();
    Check(editablePatch.Columns == 3 && editablePatch.Rows == 3 && editablePatch.ControlPoints.Count == 9
        && editablePatch.Id == Guid.Parse("18365f80-bc63-4e93-a798-3b52ab89e4e5")
        && editablePatch.Tessellation == 2 && !editablePatch.Solid
        && editablePatch.Material == 0
        && editablePatch.ControlPoints[4][1] == 1
        && editablePatch.ControlPoints[4][3] == 0.5f
        && editablePatch.ControlPoints[4][4] == 0.5f,
        "patchDef2 preserves source column/row order, control height, UVs, identity and flags");
    var patchFaces = GeometryCompiler.Compile(editablePatch, 16f);
    Check(patchFaces.Count == 8
        && patchFaces.SelectMany(face => face.Points).Any(p =>
            Math.Abs(p.X - 1) < .0001 && Math.Abs(p.Y - .25f) < .0001
                && Math.Abs(p.Z + 1) < .0001)
        && patchFaces.SelectMany(face => face.Texcoords).Any(p =>
            Math.Abs(p.X - 8) < .0001 && Math.Abs(p.Y - 8) < .0001),
        "biquadratic curve, tangent geometry and UVs evaluate at the exact midpoint");
    var renderOnly = new BuiltMap(curved.Definition);
    GeometryCompiler.Add(renderOnly, curved.Definition);
    Check(renderOnly.Faces.Count == 8 && renderOnly.Solid.Count == 0,
        "explicit render-only Quake patch does not accidentally create gameplay collision");
    editablePatch.Solid = true;
    var collisionEnabled = new BuiltMap(curved.Definition);
    GeometryCompiler.Add(collisionEnabled, curved.Definition);
    Check(collisionEnabled.Solid.Count == collisionEnabled.Faces.Count
        && collisionEnabled.Solid.Count == 8,
        "opted-in patch collision uses the exact rendered tessellation triangles");
    editablePatch.Solid = false;

    string patchNative = Path.Combine(root, "quad-native.json");
    curved.Definition.Save(patchNative);
    var nativeReloaded = MapDefinition.Load(patchNative);
    Check(nativeReloaded.Geometry.Single() is MapBezierPatch savedPatch
        && savedPatch.Columns == 3 && savedPatch.ControlPoints.Count == 9
        && savedPatch.ControlPoints[4][1] == 1 && !savedPatch.Solid,
        "native JSON save/load keeps all editable patch controls instead of triangulating");

    string patchAgainSource = Path.Combine(root, "curve-export.map");
    string patchAgainSidecar = Path.Combine(root, "curve-export-sidecar.json");
    QuakeMapInterchange.Export(curved.Definition, patchAgainSource,
        patchAgainSidecar, 16);
    var patchAgain = QuakeMapInterchange.Import(patchAgainSource, patchAgainSidecar);
    var patchReimported = (MapBezierPatch)patchAgain.Definition.Geometry.Single();
    Check(patchReimported.Id == editablePatch.Id
        && patchAgain.Definition.MapId == curved.Definition.MapId
        && patchReimported.Columns == editablePatch.Columns
        && patchReimported.Rows == editablePatch.Rows
        && patchReimported.Tessellation == editablePatch.Tessellation
        && patchReimported.Solid == editablePatch.Solid
        && patchReimported.ControlPoints.Zip(editablePatch.ControlPoints,
            (a,b) => a.Zip(b,(x,y) => Math.Abs(x-y)).All(delta => delta < .00001f)).All(x=>x),
        "patchDef2 export/import retains MapId, control points, UVs, collision and tessellation");

    var lowered = MapProjectSerializer.Clone(curved.Definition);
    Check(MapPackageGeometryCompatibility.LowerInPlace(lowered)
        && lowered.Geometry.Single() is MapMesh portablePatch
        && portablePatch.FaceTexcoords.Count == 8
        && portablePatch.Faces.Count == 8
        && portablePatch.Vertices.Count < portablePatch.Faces.Count * 3
        && !portablePatch.Solid
        && lowered.Serialize().Contains("\"kind\": \"mesh\"", StringComparison.Ordinal)
        && !lowered.Serialize().Contains("bezier-patch",StringComparison.Ordinal)
        && curved.Definition.Geometry.Single() is MapBezierPatch,
        ".ppmap compatibility adapter lowers to existing explicit-UV MapMesh without modifying source");
    Check(!MapPackageGeometryCompatibility.LowerInPlace(lowered),
        "legacy-compatible package geometry is stable without a second conversion");

    string patch3Source = Path.Combine(root, "unsupported-patchdef3.map");
    File.WriteAllText(patch3Source, File.ReadAllText(patchSource)
        .Replace("patchDef2", "patchDef3", StringComparison.Ordinal));
    refused = false;
    try { QuakeMapInterchange.Import(patch3Source); }
    catch (QuakeMapInterchangeException ex)
    { refused = ex.Diagnostics.Any(d => d.Code == "FP-RADIANT-PATCH"); }
    Check(refused, "unsupported patchDef3 is visibly refused instead of silently flattened");
    string invalidGrid = Path.Combine(root, "even-patch-grid.map");
    File.WriteAllText(invalidGrid, File.ReadAllText(patchSource)
        .Replace("( 3 3 0 0 0 )", "( 4 3 0 0 0 )", StringComparison.Ordinal));
    refused = false;
    try { QuakeMapInterchange.Import(invalidGrid); }
    catch (QuakeMapInterchangeException ex)
    { refused = ex.Diagnostics.Any(d => d.Code == "FP-RADIANT-PATCH"); }
    Check(refused, "malformed even-sized control grids are refused before geometry publication");

    // External NetRadiant edits are independent source-of-truth changes.
    // A stale sidecar must not grant permission to overwrite those bytes.
    string watched = Path.Combine(root, "external-edit.map");
    string watchedSidecar = Path.Combine(root, "external-edit-sidecar.json");
    QuakeMapInterchange.Export(map, watched, watchedSidecar);
    File.AppendAllText(watched, "\n// NetRadiant edit after previous fingerprint\n");
    string editedSource = File.ReadAllText(watched);
    string oldSidecar = File.ReadAllText(watchedSidecar);
    refused = false;
    try { QuakeMapInterchange.Export(map, watched, watchedSidecar); }
    catch (IOException) { refused = true; }
    Check(refused && File.ReadAllText(watched) == editedSource
        && File.ReadAllText(watchedSidecar) == oldSidecar,
        "external NetRadiant source changes cannot be silently overwritten by Studio");

    string unmanaged = Path.Combine(root, "unmanaged-source.map");
    File.WriteAllText(unmanaged, "Creator's independent Quake source file");
    string missingSidecar = Path.Combine(root, "absent-sidecar.json");
    refused = false;
    try { QuakeMapInterchange.Export(map, unmanaged, missingSidecar); }
    catch (IOException) { refused = true; }
    Check(refused && File.ReadAllText(unmanaged)
        == "Creator's independent Quake source file" && !File.Exists(missingSidecar),
        "existing untracked .map files require a matching identity sidecar before overwrite");

    var invalidMaterialProject = MapProjectSerializer.Clone(rampMap);
    ((MapConvexBrush)invalidMaterialProject.Geometry.Single()).Material = 999;
    string invalidMaterialOutput = Path.Combine(root, "bad-material.map");
    refused = false;
    try { QuakeMapInterchange.Export(invalidMaterialProject, invalidMaterialOutput); }
    catch (QuakeMapInterchangeException ex)
    { refused = ex.Diagnostics.Any(d => d.Code == "FP-RADIANT-EXPORT-MATERIAL"); }
    Check(refused && !File.Exists(invalidMaterialOutput),
        "invalid convex material indices fail with a typed diagnostic, not index exception");

    map.Geometry.Add(new MapWedge());
    refused = false;
    try { QuakeMapInterchange.Export(map, Path.Combine(root, "refused.map")); }
    catch (QuakeMapInterchangeException ex)
    { refused = ex.Diagnostics.Any(d => d.Code == "FP-RADIANT-EXPORT-GEOMETRY"); }
    Check(refused && !File.Exists(Path.Combine(root, "refused.map")),
        "native non-axial authoring is not destructively exported as a fake box");

    Console.WriteLine($"Quake editable interchange checks: {checks} PASS.");
}
finally
{
    try { Directory.Delete(root, true); } catch (IOException) { }
}

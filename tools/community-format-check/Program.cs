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

    string uv = Path.Combine(root, "uv-unsupported.map");
    File.WriteAllText(uv, File.ReadAllText(path).Replace(
        " 0 0 0 1 1 0 0 0", " 4 0 0 1 1 0 0 0", StringComparison.Ordinal));
    bool refused = false;
    try { QuakeMapInterchange.Import(uv); }
    catch (QuakeMapInterchangeException ex)
    {
        refused = ex.Diagnostics.Any(d => d.Code == "FP-RADIANT-UV");
    }
    Check(refused, "non-default UV projection cannot silently flatten to an untextured box");

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

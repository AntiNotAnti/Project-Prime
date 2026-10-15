using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using MphRead.Mods.MapGen;

int checks = 0;
void Check(bool condition, string message)
{
    checks++;
    if (!condition) throw new Exception(message);
}
string root = Path.Combine(Path.GetTempPath(), "prime-radiant-layer-check-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
try
{
    var box = new MapBox { Id = Guid.NewGuid(), Layer = "Old layer", Transform = new() { Scale = new[] { 4f, 2f, 4f } } };
    var mesh = new MapMesh
    {
        Id = Guid.NewGuid(), Layer = "Old layer", Label = "Native mesh", Locked = true,
        Vertices = new() { new[] { 0f, 0f, 0f }, new[] { 2f, 0f, 0f }, new[] { 0f, 0f, 2f } },
        Faces = new() { new[] { 0, 1, 2 } }
    };
    var patch = new MapBezierPatch
    {
        Id = Guid.NewGuid(), Layer = "Old layer",
        ControlPoints = Enumerable.Range(0, 9).Select(i => new[] { (float)(i / 3), 0f, (float)(i % 3), i / 3f, i % 3f }).ToList()
    };
    var legacy = new MapBrush
    {
        Id = Guid.NewGuid(), Label = "Legacy", Min = new[] { 8f, 0f, 0f }, Max = new[] { 10f, 2f, 2f }
    };
    var map = new MapDefinition
    {
        MapId = Guid.NewGuid(), Name = "LAYER_CHECK", InGameName = "Layer check",
        Geometry = new() { box, mesh, patch }, Brushes = new() { legacy },
        Materials = new() { new MapMaterial { Id = Guid.NewGuid(), Name = "textures/prime/check", SourceMaterial = 2, TexScale = 16 } }
    };
    string source = RadiantSourceWorkspace.Prepare(map, root);
    // Fixture edits below use LF anchors; exported source uses the host newline.
    // Exercise both native file conventions explicitly after constructing fixtures.
    string original = File.ReadAllText(source).ReplaceLineEndings("\n");
    const string oldDefinition = "//@$& layerdef \"Old layer\" -1 0 0 0";
    Check(original.Contains("//@$& layerdef \"Architecture\" -1 0 0 0"), "Editor export has native default layer.");
    Check(original.Contains(oldDefinition), "Editor export exposes initial canonical layer in native Layers UI.");
    Check(original.Contains("patchDef2") && Regex.Matches(original, @"(?m)^primeMeshDef\r?$").Count == 2,
        "Layer fixture retains a real Quake brush and patch alongside the native mesh and legacy recovery mesh.");
    Check(Regex.Matches(original, @"(?m)^//@\$& layer 1\r?$").Count == 3, "Each authored primitive has a native layer assignment.");
    Check(original.Contains("//@$& layer 0"), "Legacy/default geometry and reference boundary use native default layer.");

    QuakeMapImportResult Import(string contents)
    {
        File.WriteAllText(source, contents);
        return QuakeMapInterchange.Import(source, RadiantSourceWorkspace.SidecarPath(source), requireDurableGeometryContainers: true);
    }
    void Assigned(QuakeMapImportResult imported, string layer)
    {
        Check(imported.Definition.Geometry.Count == 3, "All three authored primitive kinds remain represented.");
        Check(imported.Definition.Geometry.All(g => g.Layer == layer), "Native layer assignment overrides ordinary and stale Prime payload layers: " + layer);
        Check(imported.ExplicitLayerIds.SetEquals(new[] { box.Id, mesh.Id, patch.Id }), "Explicit layer IDs identify exactly the represented authored geometry.");
        Check(imported.Definition.Brushes.Single().Id == legacy.Id, "Unedited legacy recovery remains lossless.");
    }
    Assigned(Import(original), "Old layer");
    string hierarchy = original.Replace(oldDefinition,
        "//@$& layerdef \"Child\" 2 12 34 56\n//@$& layerdef \"Parent\" -1 90 80 70");
    Assigned(Import(hierarchy), "Parent/Child");
    Check(File.ReadAllText(source) == hierarchy, "Import preserves external native hierarchy and RGB colors byte-for-byte.");
    string persistent = Regex.Replace(hierarchy, @"(?m)^//@\$& layer \d+\r?\n", "");
    persistent = persistent.Replace("\n{\n\"classname\" \"worldspawn\"", "\n//@$& layer 1\n{\n\"classname\" \"worldspawn\"");
    Check(Regex.Matches(persistent, @"(?m)^//@\$& layer 1\r?$").Count == 1,
        "Persistent-layer fixture contains exactly one native assignment before worldspawn.");
    Assigned(Import(persistent), "Parent/Child");
    string defaults = Regex.Replace(original, @"(?m)^//@\$& layer \d+\r?\n", "")
        .Replace("layerdef \"Architecture\"", "layerdef \"0\"");
    Assigned(Import(defaults), "Architecture");
    string noMetadata = Regex.Replace(original, @"(?m)^//@\$& [^\r\n]*\r?\n", "");
    var oldSource = Import(noMetadata);
    Check(oldSource.ExplicitLayerIds.Count == 0, "Legacy sources without native metadata retain adjunct ownership.");
    Check(oldSource.Definition.Geometry.Single(g => g.Id == mesh.Id).Layer == "Old layer", "Legacy native mesh retains its original payload layer.");
    var plain = Import(hierarchy.Replace("//@$& ", ""));
    Check(plain.Definition.Geometry.All(g => g.Layer == "Parent/Child"), "Native layer tokens are parsed with or without their compatibility marker.");
    string explicitZero = noMetadata.Replace("\n{\n\"classname\" \"worldspawn\"", "\n//@$& layer 0\n{\n\"classname\" \"worldspawn\"");
    var zeroOnly = Import(explicitZero);
    Check(zeroOnly.Definition.Geometry.All(g => g.Layer == "Architecture"), "Explicit native index zero without definitions uses canonical Architecture.");
    foreach (string newline in new[] { "\n", "\r\n" })
    {
        string nativeHierarchy = hierarchy.ReplaceLineEndings(newline);
        Assigned(Import(nativeHierarchy), "Parent/Child");
        Check(File.ReadAllText(source) == nativeHierarchy,
            "Import preserves native hierarchy, RGB colors and LF/CRLF line endings byte-for-byte.");
        Assigned(Import(persistent.ReplaceLineEndings(newline)), "Parent/Child");
        Assigned(Import(explicitZero.ReplaceLineEndings(newline)), "Architecture");
    }
    const string stockDefault = "//@$& layerdef \"0\" -1 0 0 0\n";
    string stockSave = stockDefault + noMetadata;
    var implicitDefault = Import(stockSave);
    Check(implicitDefault.ExplicitLayerIds.Count == 0, "Stock default-only native saves do not claim canonical layer ownership.");
    Check(implicitDefault.Definition.Geometry.Single(g => g.Id == mesh.Id).Layer == "Old layer", "Stock default-only native save preserves the original Prime mesh layer.");
    var implicitReview = RadiantSourceWorkspace.Preview(map, source: Write(stockSave));
    Check(implicitReview.Proposed.Geometry.All(g => g.Layer == "Old layer"), "Stock default-only native save preserves ordinary brush and patch adjunct layers.");
    Assigned(Import(stockSave.Replace("\n{\n\"classname\" \"worldspawn\"", "\n//@$& layer 0\n{\n\"classname\" \"worldspawn\"")), "Architecture");
    Assigned(Import(stockSave.Replace("layerdef \"0\"", "layerdef \"Architecture\"")), "Architecture");
    var colorOnly = Import(stockSave.Replace("-1 0 0 0", "-1 12 34 56"));
    Check(colorOnly.ExplicitLayerIds.Count == 0 && colorOnly.Definition.Geometry.Single(g => g.Id == mesh.Id).Layer == "Old layer",
        "Changing only the stock layer color preserves canonical membership.");

    var closeQuarters = new MapDefinition
    {
        MapId = Guid.NewGuid(), Name = "LAYER_LEGACY_NATIVE_SAVE", Materials = map.Materials,
        Geometry = new()
        {
            new MapBox { Id = Guid.NewGuid(), Label = "Legacy ordinary box", Layer = "Floors", Transform = new() { Scale = new[] { 4f, 2f, 4f } } },
            new MapMesh
            {
                Id = Guid.NewGuid(), Label = "Close Quarters.glb", Layer = "Imported Models / Close Quarters.glb", Locked = true,
                Vertices = new() { new[] { 0f, 0f, 0f }, new[] { 2f, 0f, 0f }, new[] { 0f, 0f, 2f } },
                Faces = new() { new[] { 0, 1, 2 } }
            }
        }
    };
    string legacyNativeSource = RadiantSourceWorkspace.Prepare(closeQuarters, Path.Combine(root, "legacy-native-save"));
    string stockNativeSave = stockDefault + Regex.Replace(File.ReadAllText(legacyNativeSource), @"(?m)^//@\$& [^\r\n]*\r?\n", "");
    File.WriteAllText(legacyNativeSource, stockNativeSave);
    var closeQuartersReview = RadiantSourceWorkspace.Preview(closeQuarters, legacyNativeSource);
    Check(closeQuartersReview.Proposed.Geometry.Single(g => g is MapMesh).Layer == "Imported Models / Close Quarters.glb",
        "Legacy native save retains the exact imported model layer path, including spaces around separators.");
    Check(closeQuartersReview.Proposed.Serialize() == closeQuarters.Serialize(), "Stock native save of pre-layer source is an exact canonical project no-op.");
    RadiantSourceWorkspace.ApplyReviewed(closeQuarters, closeQuartersReview);
    RadiantSourceWorkspace.Accept(closeQuarters, closeQuartersReview);
    Check(File.ReadAllText(legacyNativeSource) == stockNativeSave, "Accepting legacy stock layer metadata preserves the external native source.");

    // The old box/patch adjunct must preserve labels, shade and locks while
    // allowing native layer assignment through the workspace review boundary.
    var reviewed = RadiantSourceWorkspace.Preview(map, source: Write(hierarchy));
    Check(reviewed.Proposed.Geometry.All(g => g.Layer == "Parent/Child"), "Workspace review honors native assignments for ordinary brushes, patches and meshes.");
    Check(reviewed.Proposed.Geometry.Single(g => g.Id == box.Id).Label == box.Label, "Workspace retains ordinary brush label metadata.");
    Check(reviewed.Proposed.Geometry.Single(g => g.Id == mesh.Id).Locked, "Workspace retains native mesh lock metadata.");
    RadiantSourceWorkspace.ApplyReviewed(map, reviewed);
    RadiantSourceWorkspace.Accept(map, reviewed);
    Check(File.ReadAllText(source) == hierarchy, "Applying and accepting a review preserves native hierarchy/color source.");
    string rebuilt = RadiantSourceWorkspace.Prepare(map, Path.Combine(root, "rebuilt"));
    string rebuiltText = File.ReadAllText(rebuilt);
    Check(rebuiltText.Contains("layerdef \"Parent\" -1 0 0 0") && rebuiltText.Contains("layerdef \"Child\" 1 0 0 0"), "Fresh editor export reconstructs canonical full paths as native hierarchy.");
    Check(QuakeMapInterchange.Import(rebuilt, RadiantSourceWorkspace.SidecarPath(rebuilt), requireDurableGeometryContainers: true)
        .Definition.Geometry.All(g => g.Layer == "Parent/Child"), "Reconstructed layer hierarchy round trips.");

    string editedLegacy = EditMeshPayload(hierarchy, legacy.Id, payload =>
    {
        JsonArray vertex = payload["mesh"]!["vertices"]![0]!.AsArray();
        vertex[0] = vertex[0]!.GetValue<float>() + .25f;
    });
    // Move only the legacy primitive to Child; its native mesh topology edit
    // makes the previously field-less legacy brush an authored MapMesh.
    int legacyStart = editedLegacy.IndexOf("\"prime_id\" \"" + legacy.Id + "\"", StringComparison.Ordinal);
    int legacyLayer = editedLegacy.IndexOf("//@$& layer 0", legacyStart, StringComparison.Ordinal);
    editedLegacy = editedLegacy[..legacyLayer] + editedLegacy[legacyLayer..].Replace("//@$& layer 0", "//@$& layer 1");
    var converted = Import(editedLegacy);
    Check(converted.Definition.Brushes.Count == 0 && converted.Definition.Geometry.Single(g => g.Id == legacy.Id) is MapMesh,
        "Edited legacy source becomes canonical MapMesh.");
    Check(converted.Definition.Geometry.Single(g => g.Id == legacy.Id).Layer == "Parent/Child"
        && converted.ExplicitLayerIds.Contains(legacy.Id), "Converted legacy mesh receives the native layer override.");

    void Reject(string contents, string message)
    {
        bool rejected = false;
        try { Import(contents); }
        catch (QuakeMapInterchangeException ex) { rejected = ex.Diagnostics.Any(d => d.Code == "FP-RADIANT-LAYER" && d.Error); }
        Check(rejected, message);
    }
    Reject(original.Replace("layer 1", "layer 99"), "Unknown native assignment index rejected.");
    Reject(original.Replace(oldDefinition, "//@$& layerdef \"Child\" 99 0 0 0"), "Unknown parent index rejected.");
    Reject(original.Replace(oldDefinition, "//@$& layerdef \"Child\" 1 0 0 0"), "Self parent cycle rejected.");
    Reject(original.Replace(oldDefinition, "//@$& layerdef \"Child\" 2 0 0 0\n//@$& layerdef \"Parent\" 1 0 0 0"), "Multi-node parent cycle rejected.");
    Reject(original.Replace(oldDefinition, "//@$& layerdef \"Child\" -2 0 0 0"), "Out-of-range negative parent rejected.");
    Reject(original.Replace(oldDefinition, "//@$& layerdef \"Child\" -1 256 0 0"), "Out-of-range native RGB rejected.");
    Reject(original.Replace(oldDefinition, "//@$& layerdef \"Child\" -1 0 -1 0"), "Negative native RGB rejected.");
    Reject(original.Replace(oldDefinition, "//@$& layerdef \"Child\" -1 0 0 .5"), "Fractional native RGB rejected.");
    Reject(original.Replace(oldDefinition, oldDefinition + "\n" + oldDefinition), "Duplicate sibling names rejected.");
    Reject(original.Replace(oldDefinition, "//@$& layerdef \"0\" -1 0 0 0").Replace("layerdef \"Architecture\"", "layerdef \"0\""), "Duplicate root names rejected.");
    Reject(original.Replace(oldDefinition, "//@$& layerdef \"Child/Other\" -1 0 0 0"), "Ambiguous layer path separator rejected.");
    Reject(original.Replace(oldDefinition, "//@$& layerdef \"Child\\Other\" -1 0 0 0"), "Unsafe native quoted escape rejected.");
    Reject(original.Replace(oldDefinition, "//@$& layerdef \"\" -1 0 0 0"), "Empty layer name rejected.");
    Reject(original.Replace(oldDefinition, "//@$& layerdef \"" + new string('n', 257) + "\" -1 0 0 0"), "Native layer token name budget enforced.");
    Reject(original.Replace("layer 1", "layer 2147483648"), "Native layer integer budget enforced.");
    Reject(original.Replace(oldDefinition, oldDefinition + " trailing"), "Native definition trailing tokens rejected.");
    Reject(original.Replace("layer 1", "layer 1 trailing"), "Native assignment trailing tokens rejected.");
    Reject(original.Replace("layer 1", "layer -1"), "Negative native assignment index rejected.");
    Reject(original.Replace("//@$& layer 1\n{", "{\n//@$& layer 1"), "Native assignment inside a primitive rejected.");
    Reject(original.Replace("//@$& layer 1", oldDefinition + "\n//@$& layer 1"), "Late native layer definition rejected.");
    Reject(original.Replace("//@$& layer 1", "//@$& unsupported 1"), "Unknown native extension rejected.");
    Reject(original.Replace("//@$& layer 1", "//@$&layer 1"), "Malformed native extension marker rejected.");
    string tooMany = string.Join("\n", Enumerable.Range(0, 257).Select(i => "//@$& layerdef \"L" + i + "\" -1 0 0 0"));
    Reject(Regex.Replace(original, @"(?m)^//@\$& layerdef [^\r\n]*\r?\n", "").Replace("\n{\n\"classname\" \"worldspawn\"", "\n" + tooMany + "\n{\n\"classname\" \"worldspawn\""), "Native layer count budget enforced.");
    string tooDeep = string.Join("\n", Enumerable.Range(0, 33).Select(i => "//@$& layerdef \"L" + i + "\" " + (i - 1) + " 0 0 0"));
    Reject(Regex.Replace(original, @"(?m)^//@\$& layerdef [^\r\n]*\r?\n", "").Replace("\n{\n\"classname\" \"worldspawn\"", "\n" + tooDeep + "\n{\n\"classname\" \"worldspawn\""), "Native hierarchy depth budget enforced regardless of cached ancestors.");

    string generic = Path.Combine(root, "generic.map");
    var genericMap = new MapDefinition
    {
        MapId = Guid.NewGuid(), Name = "GENERIC_LAYER_CHECK", Geometry = new() { new MapBox { Layer = "Generic \\\"/ metadata", Transform = new() { Scale = new[] { 4f, 2f, 4f } } } },
        Materials = map.Materials
    };
    QuakeMapInterchange.Export(genericMap, generic);
    Check(!File.ReadAllText(generic).Contains("//@$&"), "Generic Quake export remains free of native layer extensions and layer validation.");
    Check(QuakeMapInterchange.Import(generic).ExplicitLayerIds.Count == 0, "Generic Quake import retains legacy layer ownership.");
    Console.WriteLine($"radiant-layer-check: {checks} checks passed");

    string Write(string contents) { File.WriteAllText(source, contents); return source; }
}
finally { Directory.Delete(root, recursive: true); }

static string EditMeshPayload(string source, Guid id, Action<JsonObject> edit)
{
    int start = source.IndexOf("\"prime_id\" \"" + id + "\"", StringComparison.Ordinal);
    Match match = new Regex("primeMeshDef\\r?\\n\\{\\r?\\nversion 1\\r?\\ndata (?<count>\\d+)\\r?\\n(?<chunks>(?:\"[A-Za-z0-9+/=]+\"\\r?\\n)+)\\}").Match(source, start);
    if (!match.Success) throw new Exception("Missing legacy primeMeshDef test primitive.");
    string encoded = string.Concat(Regex.Matches(match.Groups["chunks"].Value, "\"(?<chunk>[A-Za-z0-9+/=]+)\"").Select(m => m.Groups["chunk"].Value));
    JsonObject payload = JsonNode.Parse(Convert.FromBase64String(encoded))!.AsObject();
    edit(payload);
    encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes(payload.ToJsonString()));
    var block = new StringBuilder("primeMeshDef\n{\nversion 1\ndata " + ((encoded.Length + 767) / 768) + "\n");
    for (int i = 0; i < encoded.Length; i += 768) block.AppendLine("\"" + encoded.Substring(i, Math.Min(768, encoded.Length - i)) + "\"");
    block.Append('}');
    return source[..match.Index] + block + source[(match.Index + match.Length)..];
}

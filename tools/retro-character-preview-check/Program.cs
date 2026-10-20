using System;
using System.IO;
using System.Linq;
using MphRead.Mods.Import;
using MphRead.Mods.Import.Retro;

try
{
    if (args.Length == 2 && args[0] == "--probe-bundle")
    {
        CharacterBundleAcceptance.ProbeOwnedSource(args[1]);
        return 0;
    }
    if (args.Length == 2 && args[0] == "--source")
    {
        var files = RetroCharacterPreviewExporter.ExportFromOwnedGame(args[1]);
        foreach (var file in files) Console.WriteLine(file);
        Console.WriteLine("Local unanimated OBJ previews only. No models were installed into gameplay.");
        return 0;
    }
    if (args.Length != 0)
    {
        Console.Error.WriteLine("Usage: retro-character-preview-check [--source /path/to/owned/Prime-or-Echoes.iso | --probe-bundle /path/to/owned-source]");
        return 2;
    }
    CharacterBundleAcceptance.RunContract();
    var chars = RetroCharacterPreviewExporter.Sources;
    Check(chars.Count == 5 && chars.Select(x=>x.Id).Distinct().Count()==5,
        "five independent local model source identities");
    Check(chars.Count(x => x.Game == ExternalGameKind.MetroidPrime) == 4 &&
        chars.Count(x => x.Game == ExternalGameKind.Echoes) == 1,
        "four Trooper looks and Dark Samus");

    var body = new Cmdl
    {
        Positions = new[] { 0f,0f,0f, 1f,0f,0f, 0f,1f,0f },
        Normals = new[] { 0f,0f,1f },
        Uv0 = new[] { 0f,0f, 1f,0f, 0f,1f },
        Materials = new[] { new Cmdl.Material() },
        Triangles = new[]
        {
            new Cmdl.Corner { P=0, N=0, T0=0 },
            new Cmdl.Corner { P=1, N=0, T0=1 },
            new Cmdl.Corner { P=2, N=0, T0=2 }
        },
        TriangleMaterial = new[] { 0 }
    };
    using var output = new StringWriter(System.Globalization.CultureInfo.InvariantCulture);
    RetroCharacterPreviewExporter.WriteObj(body, output);
    string obj = output.ToString();
    Check(obj.Contains("v 1 0 0") && obj.Contains("vt 1 0") &&
        obj.Contains("f 1/1/1 2/2/1 3/3/1") &&
        obj.Contains("g material_0"), "roundtrip-ready indexed OBJ geometry");
    var bad = new Cmdl
    {
        Positions = new[] { 0f,0f,0f,1f,0f,0f,0f,1f,0f },
        Normals = body.Normals, Uv0 = body.Uv0, Materials = body.Materials,
        Triangles = new[]
        {
            new Cmdl.Corner { P=30 }, new Cmdl.Corner { P=1 }, new Cmdl.Corner { P=2 }
        },
        TriangleMaterial = new[]{0}
    };
    Expect<InvalidDataException>(() =>
        RetroCharacterPreviewExporter.WriteObj(bad, new StringWriter()),
        "source triangle outside vertex bounds");
    // Exact CSKR source weights, deduplicated by their real repeated group
    // references, are necessary to rig the offline preview mesh later.
    var group0 = new (uint Bone, float Weight)[] { (10, 1f) };
    var group1 = new (uint Bone, float Weight)[] { (10, 0.4f), (11, 0.6f) };
    body.Skin = new System.Collections.Generic.List<(uint Bone, float Weight)[]> {
        group0, group0, group1
    };
    using var weightsOutput = new MemoryStream();
    RetroCharacterSkinWeights.Write(body, weightsOutput);
    using var weightsDoc = System.Text.Json.JsonDocument.Parse(weightsOutput.ToArray());
    var weights = weightsDoc.RootElement;
    Check(weights.GetProperty("vertexCount").GetInt32() == 3 &&
        weights.GetProperty("groups").GetArrayLength() == 2 &&
        weights.GetProperty("vertexGroupIds").EnumerateArray()
            .Select(x => x.GetInt32()).SequenceEqual(new[]{0,0,1}) &&
        weights.GetProperty("groups")[1][1].GetProperty("boneId").GetUInt32() == 11,
        "lossless per-vertex bone influence sidecar uses bounded shared groups");
    body.Skin = new System.Collections.Generic.List<(uint Bone, float Weight)[]> {
        group0, group0, new (uint Bone, float Weight)[] { (99, float.NaN) }
    };
    Expect<InvalidDataException>(() =>
        RetroCharacterSkinWeights.Write(body, new MemoryStream()),
        "nonfinite source weight rejected without publish");
    body.Skin = new System.Collections.Generic.List<(uint Bone, float Weight)[]> { group0 };
    Expect<InvalidDataException>(() =>
        RetroCharacterSkinWeights.Write(body, new MemoryStream()),
        "incomplete skin cannot be used as a rig");

    Console.WriteLine("PASS: five source character identities, safe local OBJ output and invalid geometry refusal");
    return 0;
}
catch(Exception ex)
{
    Console.Error.WriteLine("FAIL: " + ex);
    return 1;
}

static void Check(bool valid, string why)
{
    if(!valid)throw new InvalidOperationException(why);
}
static void Expect<T>(Action action, string why) where T : Exception
{
    try{action();}catch(T){return;}
    throw new InvalidOperationException("Expected "+typeof(T).Name+" for "+why);
}
